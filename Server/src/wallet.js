// The one wallet, kept like the website's (worker.js:356-669, 755-790): every coin is a row in coin_tx and a balance is
// always SUM(amount), so there's no stored number to edit or drift. The same (kind, ref) pays or charges only once.
// Book Fair tickets are a second ledger (fair_tx) with the same rules, and econ_state holds the fair's counters.
// Rewards and prices come from economy.json (tools/export_econ.js, the same file the game reads), never from the client.
// Routes: GET /wallet, POST /wallet/earn {kind}, POST /wallet/spend {kind, item, ref, amount | n},
// POST /wallet/bank {session, days, total} (coins from offline play, checked against the daily find caps).

import ECONOMY from './economy.json';
import { etDay, Problem, json, readJson } from './db.js';

export { etDay };

const COINS = { table: 'coin_tx', short: 'Not enough coins' };
const TICKETS = { table: 'fair_tx', short: 'Not enough Book Fair tickets' };
const REF = /^[a-z0-9:_-]{6,60}$/i;

/** The day before (or n days after) a YYYY-MM-DD day, counted on the calendar so clock changes can't skip one. */
const shiftDay = (day, n) => new Date(Date.parse(day + 'T12:00:00Z') + n * 86400e3).toISOString().slice(0, 10);

// ---- the ledgers ----

async function sum(env, L, pid) {
  const r = await env.DB.prepare(`SELECT COALESCE(SUM(amount), 0) AS b FROM ${L.table} WHERE player_id = ?1`).bind(pid).first();
  return r ? r.b : 0;
}

// the statement that pays a row once per (kind, ref)
const addRow = (env, L, pid, kind, ref, amount) =>
  env.DB.prepare(`INSERT OR IGNORE INTO ${L.table} (player_id, kind, ref, amount, day, created_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6)`)
    .bind(pid, kind, String(ref), Math.floor(amount), etDay(), Date.now());

// a whole positive amount, once per (kind, ref): true when it was paid now
async function add(env, L, pid, kind, ref, amount) {
  if (!(amount > 0)) return false;
  const r = await addRow(env, L, pid, kind, ref, amount).run();
  return !!(r.meta && r.meta.changes);
}

// Two checks: the row only goes in while the balance covers it, then a re-sum takes it back out if a parallel
// request got there first. {ok} or {ok: false, error, status}: 409 when this ref was already paid for, else 402.
async function take(env, L, pid, kind, ref, amount) {
  amount = Math.floor(amount);
  if (!(amount > 0)) return { ok: false, error: 'Bad amount', status: 400 };
  const r = await env.DB.prepare(
    `INSERT OR IGNORE INTO ${L.table} (player_id, kind, ref, amount, day, created_at)
     SELECT ?1, ?2, ?3, ?4, ?5, ?6 WHERE (SELECT COALESCE(SUM(amount), 0) FROM ${L.table} WHERE player_id = ?1) >= ?7`,
  ).bind(pid, kind, String(ref), -amount, etDay(), Date.now(), amount).run();
  if (!r.meta || !r.meta.changes) {
    const dup = await env.DB.prepare(`SELECT 1 FROM ${L.table} WHERE player_id = ?1 AND kind = ?2 AND ref = ?3`).bind(pid, kind, String(ref)).first();
    return dup ? { ok: false, error: 'Already paid for', status: 409 } : { ok: false, error: L.short, status: 402 };
  }
  if ((await sum(env, L, pid)) < 0) {
    await env.DB.prepare(`DELETE FROM ${L.table} WHERE player_id = ?1 AND kind = ?2 AND ref = ?3`).bind(pid, kind, String(ref)).run();
    return { ok: false, error: L.short, status: 402 };
  }
  return { ok: true };
}

/** Coins: the balance, an idempotent credit (true when paid now) and a debit that never goes below 0. */
export const balance = (env, pid) => sum(env, COINS, pid);
export const credit = (env, pid, kind, ref, amount) => add(env, COINS, pid, kind, ref, amount);
export const debit = (env, pid, kind, ref, amount) => take(env, COINS, pid, kind, ref, amount);

/** Book Fair tickets: the same three, on their own ledger. */
export const tickets = (env, pid) => sum(env, TICKETS, pid);
export const fairCredit = (env, pid, kind, ref, amount) => add(env, TICKETS, pid, kind, ref, amount);
export const fairDebit = (env, pid, kind, ref, amount) => take(env, TICKETS, pid, kind, ref, amount);

/**
 * How many coin rows of one kind today, and what they add up to: {n, s}. Daily caps count these; "n" for finds also
 * counts the finds banked from offline play today, so the town and the bank share one cap.
 */
export async function todayCount(env, pid, kind) {
  const day = etDay();
  const r = (await env.DB.prepare('SELECT COUNT(*) AS n, COALESCE(SUM(amount), 0) AS s FROM coin_tx WHERE player_id = ?1 AND kind = ?2 AND day = ?3')
    .bind(pid, kind, day).first()) || { n: 0, s: 0 };
  if (kind === 'find') r.n += await bankedFinds(env, pid, day);
  return r;
}

// the finds banked from offline play for one day
const bankedFinds = async (env, pid, day) =>
  (await env.DB.prepare('SELECT finds FROM bank_days WHERE player_id = ?1 AND day = ?2').bind(pid, day).first())?.finds || 0;

/** The Book Fair counters (pinballs, wheel tickets, prism drops, pity, fishing, upgrade levels, free capsule day), made with zeros on first use. */
export async function econ(env, pid) {
  await env.DB.prepare("INSERT OR IGNORE INTO econ_state (player_id, pity, balls, tix, drops, fish_at, ups, freecap) VALUES (?1, 0, 0, 0, 0, 0, '{}', '')").bind(pid).run();
  const s = await env.DB.prepare('SELECT pity, balls, tix, drops, fish_at, ups, freecap FROM econ_state WHERE player_id = ?1').bind(pid).first();
  let ups = {};
  try { ups = JSON.parse(s.ups || '{}') || {}; } catch {}
  return { ...s, ups };
}

export const saveEcon = (env, pid, st) =>
  env.DB.prepare('UPDATE econ_state SET pity = ?2, balls = ?3, tix = ?4, drops = ?5, fish_at = ?6, ups = ?7, freecap = ?8 WHERE player_id = ?1')
    .bind(pid, st.pity, st.balls, st.tix, st.drops, st.fish_at, JSON.stringify(st.ups), st.freecap || '').run();

// the latest daily gift: [day, run]
async function lastGift(env, pid) {
  const g = await env.DB.prepare("SELECT ref FROM coin_tx WHERE player_id = ?1 AND kind = 'gift' ORDER BY ref DESC LIMIT 1").bind(pid).first();
  const [day, run] = g ? String(g.ref).split(':') : ['', '0'];
  return [day, parseInt(run, 10) || 0];
}

// What the player has bought for keeps: keepsakes, class unlocks, library levels and moves (the ledger keeps one row per
// purchase, its ref the item), and the last two days' stock pieces. The site keeps these in its synced save.
const ownedRows = (env, pid, today) => env.DB.prepare(
  `SELECT ref FROM coin_tx WHERE player_id = ?1 AND kind = 'shop'
   AND (ref LIKE 'decor:%' OR ref LIKE 'unlock:%' OR ref LIKE 'meta:%' OR ref LIKE 'move:%' OR (ref LIKE 'stock:%' AND day >= ?2))`,
).bind(pid, shiftDay(today, -1)).all();

/** What every wallet reply carries: balances, today's gift and caps, the fair's counters, what's owned and the last 20 coin rows. */
export async function payload(env, pid) {
  const today = etDay();
  const [coins, fair, st, [giftDay, run], recent, used, owned, bankedToday] = await Promise.all([
    balance(env, pid), tickets(env, pid), econ(env, pid), lastGift(env, pid),
    env.DB.prepare('SELECT kind, amount, day, created_at FROM coin_tx WHERE player_id = ?1 ORDER BY id DESC LIMIT 20').bind(pid).all(),
    env.DB.prepare('SELECT kind, COUNT(*) AS n FROM coin_tx WHERE player_id = ?1 AND day = ?2 GROUP BY kind').bind(pid, today).all(),
    ownedRows(env, pid, today), bankedFinds(env, pid, today),
  ]);
  const usedToday = Object.fromEntries((used.results || []).map((u) => [u.kind, u.n]));
  if (bankedToday) usedToday.find = (usedToday.find || 0) + bankedToday;
  return {
    balance: coins, fair, day: today, boost: 100,
    gift: { claimed: giftDay === today, run: giftDay === today || giftDay === shiftDay(today, -1) ? run : 0 },
    used: usedToday,
    balls: st.balls, tix: st.tix, drops: st.drops, pity: st.pity, ups: st.ups, freecap: st.freecap === today,
    owned: (owned.results || []).map((o) => o.ref), recent: recent.results || [],
  };
}

// ---- routes ----

/** GET /wallet and POST /wallet/earn, /wallet/spend and /wallet/bank. Each answers with the wallet payload plus what happened. */
export async function walletRoute(request, env, path, me) {
  const method = request.method;
  if (path === '/wallet' && method === 'GET') {
    await credit(env, me.id, 'starter', 'once', ECONOMY.starter);
    return reply(env, me.id);
  }
  if (method !== 'POST' || !Object.hasOwn(POSTS, path)) throw new Problem('Not found', 404);
  const body = await readJson(request);
  await credit(env, me.id, 'starter', 'once', ECONOMY.starter);
  return reply(env, me.id, await POSTS[path](env, me.id, body));
}

const POSTS = { '/wallet/earn': earn, '/wallet/spend': spend, '/wallet/bank': bank };

const reply = async (env, pid, extra = {}) => json({ ...(await payload(env, pid)), ...extra });

/** {kind: 'gift'}: the daily gift. Each day in a row pays the next step of the 7-day track, then it starts again. */
async function earn(env, pid, body) {
  if (String(body.kind || '') !== 'gift') throw new Problem('Unknown reward');
  const today = etDay();
  const [day, last] = await lastGift(env, pid);
  if (day === today) throw new Problem('Already claimed today', 409);
  const run = day === shiftDay(today, -1) ? last + 1 : 1;
  const granted = ECONOMY.loginTrack[(run - 1) % ECONOMY.loginTrack.length];
  if (!(await credit(env, pid, 'gift', `${today}:${run}`, granted))) throw new Problem('Already claimed today', 409);
  if (run % 7 === 0) {                                     // day 7 also owes a free Book Fair capsule today
    const st = await econ(env, pid);
    st.freecap = today;
    await saveEcon(env, pid, st);
  }
  return { granted, run };
}

/**
 * {kind: 'shop', item, ref}: a purchase priced here (priceOf; any "amount" is ignored). Items that sell once use the item
 * as the ref; the others need "item:<suffix>" (a nonce, or a pet id for a pet's move). {kind: 'fair', n, ref?}: coins for
 * a pack of n Book Fair tickets.
 */
async function spend(env, pid, body) {
  const kind = String(body.kind || 'shop');
  if (kind === 'shop') {
    const item = String(body.item || '');
    const { price, once } = priceOf(item);
    const ref = once ? item : String(body.ref || '');
    if (!REF.test(ref) || (!once && !ref.startsWith(item + ':'))) throw new Problem('Bad purchase');
    paid(await debit(env, pid, 'shop', ref, price));
    return { paid: price };
  }
  if (kind === 'fair') {
    const n = parseInt(body.n, 10), pack = ECONOMY.fair.packs.find(([k]) => k === n);
    if (!pack) throw new Problem('Unknown ticket pack');
    const ref = REF.test(String(body.ref || '')) ? String(body.ref) : `${Date.now()}:${Math.floor(Math.random() * 1e6)}`;
    const d = await debit(env, pid, 'fairbuy', ref, pack[1]);
    if (d.status !== 409) paid(d);                         // already paid with this ref: make sure the tickets arrived
    await fairCredit(env, pid, 'buy', ref, n);
    return { paid: d.ok ? pack[1] : 0, got: n };
  }
  throw new Problem('Unknown purchase');
}

const paid = (d) => { if (!d.ok) throw new Problem(d.error, d.status); };

/**
 * A shop item's coin price and whether it sells only once per account (worker.js has the client send prices; here
 * the server looks them up). decor:<id> keepsake · move:<cls>:<move> · unlock:<cls> · meta:<upgrade>:<new level> ·
 * stock:<day>:<town>:<i> (re-rolled here) · satchel:<town>. Unity's class changes are free, so they aren't sold.
 */
export function priceOf(item) {
  const S = ECONOMY.shop, p = item.split(':'), own = (o, k) => (Object.hasOwn(o, k) ? o[k] : undefined);
  const at = (k, n) => p[0] === k && p.length === n;
  let price, once = true;
  if (at('decor', 2)) price = own(S.decor, p[1])?.p;
  else if (at('unlock', 2)) price = own(S.unlock, p[1]);
  else if (at('meta', 3)) price = metaPrice(p[1], p[2]);
  else if (at('stock', 4)) price = stockPrice(p[1], p[2], p[3]);
  else if (at('move', 3)) { price = own(S.move, `${p[1]}:${p[2]}`); once = false; }
  else if (at('satchel', 2)) { price = S.satchel; once = false; }
  if (!(price > 0)) throw new Problem('Unknown purchase');
  return { price, once };
}

// a library upgrade's next level costs 80·lvl + 20·(lvl−1)² (06-tales.js:403, with lvl the level it becomes)
function metaPrice(k, level) {
  const m = ECONOMY.shop.meta.find((x) => x.k === k), lvl = Number(level);
  return m && Number.isInteger(lvl) && lvl >= 1 && lvl <= m.max ? 80 * lvl + 20 * (lvl - 1) ** 2 : 0;
}

// FNV-1a over UTF-16 units of each code point, and mulberry32: the site's ltHash and ltRng (14-extras.js:298, 429)
const fnv = (s) => { let x = 2166136261; for (const ch of String(s)) { x ^= ch.charCodeAt(0); x = Math.imul(x, 16777619); } return x >>> 0; };
const mulberry32 = (seed) => { let a = seed >>> 0; return () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; };

/**
 * Today's satchel stock (11-plaza.js:1376-1378): every town lays out 4 pieces a day, rolled from a stream seeded by
 * "stock:<day>:<town>". Only the rarity decides the price, so this re-rolls it the same way and skips the stream past
 * lootRoll's other draws (slot and base or legendary 2, ancient 1 from Rare up, seed 1). The day is the buyer's own
 * date, so it may be a day either side of the server's.
 */
function stockPrice(day, town, index) {
  const R = ECONOMY.shop.stock, lv = Object.hasOwn(R.lv, town) ? R.lv[town] : 0, i = Number(index), today = etDay();
  if (!lv || !Number.isInteger(i) || i < 0 || i > 3 || ![shiftDay(today, -1), today, shiftDay(today, 1)].includes(day)) return 0;
  const rnd = mulberry32(fnv(`stock:${day}:${town}`)), w = R.w.map((x, k) => x * R.m[k]), total = w.reduce((a, b) => a + b, 0);
  for (let j = 0; ; j++) {
    let r = rnd() * total, t = 0;
    while (t < 4 && r >= w[t]) { r -= w[t]; t++; }
    if (j === i) return Math.round(R.base[t] * (1 + Math.max(1, Math.min(60, 4 + lv * 2 + j)) / 40) / 5) * 5;
    for (let k = t >= 2 ? 4 : 3; k > 0; k--) rnd();
  }
}

// ---- banking offline coins ----

const SESSION = /^[A-Za-z0-9-]{8,40}$/;
const BANK_DAYS = 14;                                       // how old a day's finds may be and still bank
const FIND_KINDS = Object.keys(ECONOMY.finds.coins).sort((a, b) => ECONOMY.finds.coins[b] - ECONOMY.finds.coins[a]);

/**
 * {session, days: {"YYYY-MM-DD": {coin, bag, gift}}, total}: banks one stretch of offline play (the game's offline purse).
 * Each day's finds fit under that day's find cap after the online finds and the finds banked before (the most valuable
 * first: gifts, then bags, then coins), and the session banks its total up to what those finds are worth, as one
 * 'bank' row. Each session banks once: sending it again answers what it banked the first time.
 */
async function bank(env, pid, body) {
  const session = String(body.session || '');
  if (!SESSION.test(session)) throw new Problem('Bad session');
  const done = await bankedSession(env, pid, session);
  if (done) return done;
  const total = body.total, days = bankDays(body.days);
  if (!Number.isSafeInteger(total) || total < 0) throw new Problem('Bad total');

  // the session's days so far: online finds (coin_tx) and finds banked by earlier sessions, as {day: n}
  const list = JSON.stringify(Object.keys(days));
  const perDay = async (sql) => Object.fromEntries(((await env.DB.prepare(sql).bind(pid, list).all()).results || []).map((x) => [x.day, x.n]));
  const [online, before] = await Promise.all([
    perDay("SELECT day, COUNT(*) AS n FROM coin_tx WHERE player_id = ?1 AND kind = 'find' AND day IN (SELECT value FROM json_each(?2)) GROUP BY day"),
    perDay('SELECT day, finds AS n FROM bank_days WHERE player_id = ?1 AND day IN (SELECT value FROM json_each(?2))'),
  ]);
  let allowed = 0;
  const kept = {};                                          // day -> the finds this session banks for it
  for (const [day, found] of Object.entries(days)) {
    let room = Math.max(0, ECONOMY.finds.perDay - (online[day] || 0) - (before[day] || 0));
    for (const k of FIND_KINDS) {
      const n = Math.min(found[k] || 0, room);
      room -= n;
      allowed += n * ECONOMY.finds.coins[k];
      if (n) kept[day] = (kept[day] || 0) + n;
    }
  }
  const banked = Math.min(total, allowed), refused = total - banked;

  // one batch, so it all happens or none of it: the session's row goes in first and fails if this session was banked meanwhile
  const writes = [
    env.DB.prepare('INSERT INTO bank_sessions (player_id, id, banked, refused, created_at) VALUES (?1, ?2, ?3, ?4, ?5)').bind(pid, session, banked, refused, Date.now()),
    ...Object.entries(kept).map(([day, n]) => env.DB.prepare(
      'INSERT INTO bank_days (player_id, day, finds) VALUES (?1, ?2, ?3) ON CONFLICT (player_id, day) DO UPDATE SET finds = finds + ?3').bind(pid, day, n)),
    env.DB.prepare('DELETE FROM bank_days WHERE player_id = ?1 AND day < ?2').bind(pid, shiftDay(etDay(), -BANK_DAYS)),
    ...(banked > 0 ? [addRow(env, COINS, pid, 'bank', `sess:${session}`, banked)] : []),
  ];
  try {
    await env.DB.batch(writes);
  } catch (e) {
    const again = await bankedSession(env, pid, session);
    if (again) return again;
    throw e;
  }
  return { session, banked, refused };
}

// what a session banked, or null when it hasn't been sent before
async function bankedSession(env, pid, session) {
  const r = await env.DB.prepare('SELECT banked, refused FROM bank_sessions WHERE player_id = ?1 AND id = ?2').bind(pid, session).first();
  return r && { session, banked: r.banked, refused: r.refused };
}

// {day: {kind: whole count}} with days from BANK_DAYS ago to tomorrow and the town's find kinds; 400 otherwise
function bankDays(days) {
  if (!days || typeof days !== 'object' || Array.isArray(days)) throw new Problem('Bad finds');
  const today = etDay(), open = new Set();
  for (let n = -BANK_DAYS; n <= 1; n++) open.add(shiftDay(today, n));
  for (const [day, found] of Object.entries(days)) {
    if (!open.has(day)) throw new Problem(`Only finds from the last ${BANK_DAYS} days can be banked`);
    if (!found || typeof found !== 'object' || Array.isArray(found)) throw new Problem('Bad finds');
    for (const [k, n] of Object.entries(found)) if (!FIND_KINDS.includes(k) || !Number.isSafeInteger(n) || n < 0) throw new Problem('Bad finds');
  }
  return days;
}

// ---- admin ----

/** Sets a player's coins to "target" with one 'admin' row for the difference. Returns [old, new]; refuses a change of 0. */
export async function adminSetCoins(env, pid, target, by) {
  const old = await balance(env, pid);
  if (target === old) throw new Problem('They already have that many coins.');
  await env.DB.prepare("INSERT INTO coin_tx (player_id, kind, ref, amount, day, created_at) VALUES (?1, 'admin', ?2, ?3, ?4, ?5)")
    .bind(pid, `${by}:${Date.now()}:${Math.floor(Math.random() * 1e6)}`, target - old, etDay(), Date.now()).run();
  return [old, target];
}

/** Statements that delete everything the wallet keeps for a player (for a batch that deletes the account). */
export const deleteWalletRows = (env, pid) =>
  ['coin_tx', 'fair_tx', 'econ_state', 'bank_sessions', 'bank_days'].map((t) => env.DB.prepare(`DELETE FROM ${t} WHERE player_id = ?1`).bind(pid));
