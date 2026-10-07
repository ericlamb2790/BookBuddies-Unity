// Tales of Pages on the server, like the website's /quest routes (worker.js:4202-4394): co-op tales saved for the
// whole party, the lobby's lists, the Bramble Road raid (one villain for everyone, worn down over a week, 3 tries a
// day each), notes to players and the daily tale's board. Solo tales stay on the player's device.
// The live party room is tales_room.js; its ticket comes from index.js (/quest/ticket).

import { cleanText } from './safety.js';
import { cleanPet } from './pets.js';
import { etDay, Problem, json, readJson } from './db.js';
import { credit, balance } from './wallet.js';

const GOING = 10;              // tales going at once, started or joined
const RAID = 'road';           // the one Bramble Road raid (the site keeps one per club)
const RAID_HP = 6000;          // the raid villain's health per raider (QUEST_RAID_HP)
const TRIES = 3;               // raid fights per player per ET day
const NEWS_MS = 20 * 60e3;     // tale news: one note per player per tale in this window

/** /quest/save|lobby|get|leave, /quest/raid|raid/hit|raid/claim, /quest/daily|daily/save, /notes and /notes/read. */
export async function questRoute(request, env, path, url, me) {
  const get = request.method === 'GET', post = request.method === 'POST';
  if (path === '/quest/save' && post) return save(env, me, await readBody(request));
  if (path === '/quest/lobby' && get) return lobby(env, me);
  if (path === '/quest/get' && get) return getRun(env, me, url.searchParams.get('id'));
  if (path === '/quest/leave' && post) return leave(env, me, await readJson(request));
  if (path === '/quest/raid' && get) return raidView(env, me, await currentRaid(env));
  if (path === '/quest/raid/hit' && post) return raidHit(env, me, await readJson(request));
  if (path === '/quest/raid/claim' && post) return raidClaim(env, me, await readJson(request));
  if (path === '/quest/daily' && get) return daily(env, me, url.searchParams.get('day'));
  if (path === '/quest/daily/save' && post) return dailySave(env, me, await readBody(request));
  if (path === '/notes' && get) return notes(env, me);
  if (path === '/notes/read' && post) return notesRead(env, me, await readJson(request));
  throw new Problem('Not found', 404);
}

// ---- shared tales ----

const inParty = (q, pid) => String(q.party || '').includes(`"${pid}"`);
const canOpen = (q, me) => !!q && (q.owner_id === me.id || q.visibility === 'link' || q.visibility === 'open' || inParty(q, me.id));

/** A shared tale this player may open (its owner, anyone with the link, or a party member), or null. */
export async function openRun(env, me, id) {
  const q = await env.DB.prepare('SELECT * FROM quest_runs WHERE id = ?1').bind(String(id || '')).first();
  return canOpen(q, me) ? q : null;
}

/**
 * The leader saves the run (worker.js:4205-4239, without clubs, private runs or the best-chapter board).
 * {ok, id, srev} · {ok: false, cap} (10 going) · {ok: false, stale, srev} (a friend saved since base) · {ok: false, closed}.
 */
async function save(env, me, body) {
  const state = typeof body.state === 'string' ? body.state : JSON.stringify(body.state || {});
  if (state.length > 380000) throw new Problem('Too big');
  const chapter = int(body.chapter, 1, 100000, 1), over = body.over ? 1 : 0, online = int(body.online, 0, 50, 0);
  const crew = (Array.isArray(body.party) ? body.party : []).slice(0, 12).map((u) => String(u).slice(0, 40));
  const vis = body.visibility === 'open' ? 'open' : 'link';
  const kind = body.kind === 'dungeon' || body.kind === 'gather' ? body.kind : null;
  const title = cleanText(body.title, 60) || 'A new tale';
  const pets = JSON.stringify((Array.isArray(body.pets) ? body.pets : []).slice(0, 4).map((p) => ({ n: cleanText(p && p.n, 30), l: look(p && p.l) })));
  const cur = body.id ? await env.DB.prepare('SELECT * FROM quest_runs WHERE id = ?1').bind(String(body.id)).first() : null;
  if (cur && !canOpen(cur, me)) throw new Problem('Not allowed', 403);
  if (cur && cur.over === 2) return json({ ok: false, closed: true });
  if (!cur && !over) {
    const going = await env.DB.prepare('SELECT COUNT(*) AS n FROM quest_runs WHERE over = 0 AND (owner_id = ?1 OR party LIKE ?2)').bind(me.id, `%"${me.id}"%`).first();
    if (going.n >= GOING) return json({ ok: false, cap: 'You have 10 tales going. Close one in Your journeys to start another.' });
  }
  // played in turns by whoever shows up: a copy loaded before a friend's save doesn't overwrite their progress
  if (cur && body.base != null && !body.force && cur.srev > (parseInt(body.base, 10) || 0) && cur.last_by && cur.last_by !== me.id) {
    return json({ ok: false, stale: true, srev: cur.srev });
  }
  const now = Date.now(), owner = !cur || cur.owner_id === me.id;   // only the owner renames the tale or changes who can join
  const name = owner ? title : cur.title;
  let id = cur && cur.id, srev = 1;
  if (!cur) {
    id = b64url(crypto.getRandomValues(new Uint8Array(6)));
    await env.DB.prepare(`INSERT INTO quest_runs (id, owner_id, title, chapter, party, state, visibility, over, created_at, updated_at, online, pets, srev, last_by, kind)
      VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?9, ?10, ?11, 1, ?2, ?12)`).bind(id, me.id, title, chapter, JSON.stringify(crew), state, vis, over, now, online, pets, kind).run();
  } else {
    srev = (await env.DB.prepare(`UPDATE quest_runs SET chapter = ?2, party = ?3, state = ?4, over = ?5, updated_at = ?6, visibility = ?7, online = ?8,
      pets = ?9, srev = srev + 1, last_by = ?10, kind = COALESCE(?11, kind), title = ?12 WHERE id = ?1 RETURNING srev`)
      .bind(id, chapter, JSON.stringify(crew), state, over, now, owner ? vis : cur.visibility, online, pets, me.id, kind, name).first()).srev;
  }
  const news = body.news && typeof body.news.m === 'string' ? cleanText(body.news.m, 140) : '';
  if (news && kind) {
    const skip = new Set((Array.isArray(body.news.skip) ? body.news.skip : []).map(String).concat(me.id));
    const to = [...new Set([...crew, cur ? cur.owner_id : me.id])].filter((u) => !skip.has(u)).slice(0, 11);
    const game = JSON.stringify({ t: '🗺️ ' + name, m: `${me.name}: ${news}`, id });
    await tell(env, to, me.id, game, `tale:${id}:${Math.floor(now / NEWS_MS)}`, now);
  }
  return json({ ok: true, id, srev });
}

/** The lobby's lists: live parties anyone can join (or mine), seen in the last 45 s, and my tales still going. */
async function lobby(env, me) {
  const mine = `%"${me.id}"%`;
  const [live, going] = await Promise.all([
    env.DB.prepare(`SELECT q.id, q.title, q.chapter, q.visibility, q.online, q.pets, q.updated_at, p.name AS owner FROM quest_runs q JOIN players p ON p.id = q.owner_id
      WHERE q.over = 0 AND q.online > 0 AND q.updated_at > ?1 AND (q.visibility = 'open' OR q.owner_id = ?2 OR q.party LIKE ?3)
      ORDER BY q.online DESC, q.updated_at DESC LIMIT 12`).bind(Date.now() - 45e3, me.id, mine).all(),
    env.DB.prepare(`SELECT q.id, q.title, q.chapter, q.visibility, q.pets, q.over, q.updated_at, q.kind, q.last_by, q.owner_id, p.name AS last_name
      FROM quest_runs q LEFT JOIN players p ON p.id = q.last_by WHERE (q.owner_id = ?1 OR q.party LIKE ?2) AND q.over = 0
      ORDER BY q.updated_at DESC LIMIT 20`).bind(me.id, mine).all(),
  ]);
  return json({ live: live.results || [], mine: going.results || [] });
}

async function getRun(env, me, id) {
  const q = await openRun(env, me, id);
  if (!q) throw new Problem('This tale is private or was removed', 404);
  return json({ ...q, mine: q.owner_id === me.id, live: true });
}

/** {id}: a member leaves the party for good (taken out of its lists and the saved state); the owner closes the tale for everyone. */
async function leave(env, me, body) {
  const id = String(body.id || '');
  const q = await env.DB.prepare('SELECT owner_id, party, state FROM quest_runs WHERE id = ?1').bind(id).first();
  if (!q) return json({ ok: true });
  if (q.owner_id === me.id) {
    await env.DB.prepare('UPDATE quest_runs SET over = 2, online = 0, updated_at = ?2 WHERE id = ?1').bind(id, Date.now()).run();
    return json({ ok: true, closed: true });
  }
  const st = parse(q.state);
  if (st && typeof st === 'object') {
    const keep = (h) => !h || h.uid !== me.id;
    if (Array.isArray(st.party)) st.party = st.party.filter(keep);
    if (Array.isArray(st.cast)) st.cast = st.cast.filter(keep);
    if (Array.isArray(st.fans)) st.fans = st.fans.filter((u) => u !== me.id);
    for (const k of ['gone', 'ready']) if (st[k] && typeof st[k] === 'object') delete st[k][me.id];
  }
  const party = (parse(q.party) || []).filter((u) => u !== me.id);
  await env.DB.prepare('UPDATE quest_runs SET party = ?2, state = COALESCE(?3, state), srev = srev + 1 WHERE id = ?1')
    .bind(id, JSON.stringify(party), st && typeof st === 'object' ? JSON.stringify(st) : null).run();
  return json({ ok: true });
}

// ---- the Bramble Road raid (worker.js:4344-4393, one raid for everyone instead of one per club) ----

/** The raid going now: a new one when there's none, the last ran out (7 days) or fell over 12 hours ago. */
async function currentRaid(env) {
  const now = Date.now();
  const R = await env.DB.prepare('SELECT * FROM quest_raids WHERE club = ?1 ORDER BY started_at DESC LIMIT 1').bind(RAID).first();
  if (R && (R.done_at ? now - R.done_at <= 12 * 3600e3 : R.ends_at >= now)) return R;
  const n = (R ? R.n : 0) + 1;
  const hitters = R ? (await env.DB.prepare('SELECT COUNT(*) AS c FROM quest_raid_hits WHERE raid_id = ?1').bind(R.id).first()).c : 0;
  const members = Math.max(2, Math.min(40, hitters)), max = members * RAID_HP + (n - 1) * members * 600, id = `${RAID}:${n}`;
  await env.DB.prepare('INSERT OR IGNORE INTO quest_raids (id, club, n, boss, lvl, hp, max, started_at, ends_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?6, ?7, ?8)')
    .bind(id, RAID, n, (n * 7 + RAID.length) % 997, Math.min(30, 4 + n * 2), max, now, now + 7 * 864e5).run();
  return env.DB.prepare('SELECT * FROM quest_raids WHERE id = ?1').bind(id).first();
}

/** {raid, top (8 best hitters), allies (3 latest other raiders' pets), my {dmg, left}, claimed} plus extra. */
async function raidView(env, me, R, extra = {}) {
  const [r, top, mine, allies, paid] = await Promise.all([
    env.DB.prepare('SELECT * FROM quest_raids WHERE id = ?1').bind(R.id).first(),
    env.DB.prepare('SELECT h.dmg, h.pet, p.name AS n FROM quest_raid_hits h JOIN players p ON p.id = h.player_id WHERE h.raid_id = ?1 AND h.dmg > 0 ORDER BY h.dmg DESC LIMIT 8').bind(R.id).all(),
    env.DB.prepare('SELECT dmg, tries, day FROM quest_raid_hits WHERE raid_id = ?1 AND player_id = ?2').bind(R.id, me.id).first(),
    env.DB.prepare('SELECT h.player_id, h.pet, p.name AS n FROM quest_raid_hits h JOIN players p ON p.id = h.player_id WHERE h.player_id != ?1 AND h.pet IS NOT NULL ORDER BY h.last_at DESC LIMIT 12').bind(me.id).all(),
    env.DB.prepare("SELECT 1 AS x FROM coin_tx WHERE player_id = ?1 AND kind = 'clubraid' AND ref = ?2").bind(me.id, R.id).first(),
  ]);
  const seen = new Set();
  const al = (allies.results || []).filter((a) => !seen.has(a.player_id) && seen.add(a.player_id)).slice(0, 3).map((a) => ({ n: a.n, pet: parse(a.pet) }));
  const used = mine && mine.day === etDay() ? mine.tries : 0;
  return json({
    raid: { id: r.id, n: r.n, boss: r.boss, lvl: r.lvl, hp: r.hp, max: r.max, ends_at: r.ends_at, done_at: r.done_at },
    top: (top.results || []).map((t) => ({ n: t.n, dmg: t.dmg, pet: parse(t.pet) })), allies: al,
    my: { dmg: (mine && mine.dmg) || 0, left: Math.max(0, TRIES - used) }, claimed: paid ? 1 : 0, ...extra,
  });
}

/** The raid unless body.id names an older one. */
async function raidFor(env, body) {
  const R = await currentRaid(env);
  if (String(body.id || '') !== R.id) throw new Problem('That raid has ended. Have a look at the new one!');
  return R;
}

/** {id, dmg, pet}: one fight's damage, capped at 15% of the pool; the final blow tells every other raider about the chest. */
async function raidHit(env, me, body) {
  const R = await raidFor(env, body);
  if (R.done_at) return raidView(env, me, R, { dealt: 0 });
  const now = Date.now(), day = etDay();
  const mine = await env.DB.prepare('SELECT tries, day FROM quest_raid_hits WHERE raid_id = ?1 AND player_id = ?2').bind(R.id, me.id).first();
  if (mine && mine.day === day && mine.tries >= TRIES) throw new Problem('No tries left today. Come back tomorrow!', 429);
  const dealt = Math.max(0, Math.min(Math.ceil(R.max * 0.15), 60000, parseInt(body.dmg, 10) || 0));
  const pet = cleanSeed(body.pet, 3000);
  await env.DB.batch([
    env.DB.prepare('UPDATE quest_raids SET hp = MAX(0, hp - ?2), done_at = CASE WHEN hp - ?2 <= 0 THEN ?3 ELSE done_at END WHERE id = ?1 AND done_at IS NULL').bind(R.id, dealt, now),
    env.DB.prepare(`INSERT INTO quest_raid_hits (raid_id, player_id, dmg, tries, day, pet, last_at) VALUES (?1, ?2, ?3, 1, ?4, ?5, ?6)
      ON CONFLICT (raid_id, player_id) DO UPDATE SET dmg = dmg + excluded.dmg, tries = CASE WHEN day = excluded.day THEN tries + 1 ELSE 1 END,
      day = excluded.day, pet = COALESCE(excluded.pet, pet), last_at = excluded.last_at`).bind(R.id, me.id, dealt, day, pet && JSON.stringify(pet), now),
  ]);
  if ((await env.DB.prepare('SELECT done_at FROM quest_raids WHERE id = ?1').bind(R.id).first()).done_at === now) {
    const game = JSON.stringify({ t: '🏰 Raid won!', m: `${me.name} landed the final blow. Claim your raid chest in Tales of Pages.`, id: 'raid' });
    await env.DB.prepare(`INSERT OR IGNORE INTO notes (to_id, from_id, kind, game, day, created_at) SELECT player_id, ?2, 'notice', ?3, ?4, ?5
      FROM quest_raid_hits WHERE raid_id = ?1 AND player_id != ?2 AND dmg > 0`).bind(R.id, me.id, game, 'raid:' + R.id, now).run();
  }
  return raidView(env, me, R, { dealt });
}

/** {id}: once the villain falls, everyone who hurt it opens a chest of 150 + up to 250 coins for their share (ledger kind 'clubraid'). */
async function raidClaim(env, me, body) {
  const R = await raidFor(env, body);
  if (!R.done_at) throw new Problem('The villain is still standing!');
  const mine = await env.DB.prepare('SELECT dmg FROM quest_raid_hits WHERE raid_id = ?1 AND player_id = ?2').bind(R.id, me.id).first();
  if (!mine || !(mine.dmg > 0)) throw new Problem('Fight the next villain to earn a chest');
  const coins = 150 + Math.min(250, Math.round(mine.dmg / R.max * 1000));
  const paid = await credit(env, me.id, 'clubraid', R.id, coins);
  return json({ coins: paid ? coins : 0, already: !paid, balance: await balance(env, me.id) });
}

// ---- the daily tale's board (worker.js:4311-4333): one row per player per calendar date ----

const utcDay = (n) => new Date(Date.now() + n * 864e5).toISOString().slice(0, 10);
// keyed by the player's own date, so yesterday to tomorrow (UTC) is a real "today" somewhere; anything else is today
const dayOf = (d) => ([utcDay(-1), utcDay(0), utcDay(1)].includes(String(d)) ? String(d) : utcDay(0));

/** ?day=: {day, rank, n, mine {score, label, over, state (until over)}, top (20 best)}. */
async function daily(env, me, asked) {
  const day = dayOf(asked);
  const [mine, top, count] = await Promise.all([
    env.DB.prepare('SELECT score, label, over, state, at FROM quest_daily WHERE day = ?1 AND player_id = ?2').bind(day, me.id).first(),
    env.DB.prepare(`SELECT d.player_id, d.score, d.label, d.pet, d.look, d.over, p.name FROM quest_daily d JOIN players p ON p.id = d.player_id
      WHERE d.day = ?1 ORDER BY d.score DESC, d.at ASC LIMIT 20`).bind(day).all(),
    env.DB.prepare('SELECT COUNT(*) AS n FROM quest_daily WHERE day = ?1').bind(day).first(),
  ]);
  const ahead = mine && await env.DB.prepare('SELECT COUNT(*) AS n FROM quest_daily WHERE day = ?1 AND (score > ?2 OR (score = ?2 AND at < ?3))').bind(day, mine.score, mine.at).first();
  return json({
    day, rank: mine ? ahead.n + 1 : null, n: count.n,
    mine: mine ? { score: mine.score, label: mine.label, over: !!mine.over, state: mine.over ? null : mine.state } : null,
    top: (top.results || []).map((x) => ({ name: x.name, score: x.score, label: x.label, pet: x.pet, look: parse(x.look), over: !!x.over, me: x.player_id === me.id })),
  });
}

/** {day, score, label, pet, look, state, over}: keeps the best score; once over the day's row is locked ({ok: false, done}). */
async function dailySave(env, me, body) {
  const day = dayOf(body.day);
  if (day !== String(body.day || '')) return json({ ok: false, late: true });
  const over = body.over ? 1 : 0, state = over ? null : JSON.stringify(body.state || {});
  if (state && state.length > 380000) throw new Problem('Too big');
  const cur = await env.DB.prepare('SELECT over FROM quest_daily WHERE day = ?1 AND player_id = ?2').bind(day, me.id).first();
  if (cur && cur.over) return json({ ok: false, done: true });
  await env.DB.prepare(`INSERT INTO quest_daily (day, player_id, score, label, pet, look, state, over, at) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)
    ON CONFLICT (day, player_id) DO UPDATE SET label = CASE WHEN excluded.score >= score THEN excluded.label ELSE label END,
    at = CASE WHEN excluded.score > score THEN excluded.at ELSE at END, score = MAX(score, excluded.score), pet = excluded.pet, look = excluded.look,
    state = excluded.state, over = excluded.over`)
    .bind(day, me.id, int(body.score, 0, 100000, 0), cleanText(body.label, 60), cleanText(body.pet, 30), JSON.stringify(look(body.look)), state, over, Date.now()).run();
  return json({ ok: true });
}

// ---- notes: tale news and the raid's final blow ----

// one 'notice' per player and day key (tale news: per tale per 20 minutes), only to players who still exist
const tell = (env, to, from, game, day, now) => to.length && env.DB.batch(to.map((u) => env.DB.prepare(
  `INSERT OR IGNORE INTO notes (to_id, from_id, kind, game, day, created_at) SELECT ?1, ?2, 'notice', ?3, ?4, ?5
   WHERE EXISTS (SELECT 1 FROM players WHERE id = ?1) AND NOT EXISTS (SELECT 1 FROM notes WHERE to_id = ?1 AND day = ?4)`).bind(u, from, game, day, now)));

/** The newest 20 unread notes: {notes: [{id, kind, game {t, m, id}, created_at}]}; game.id is a tale id or 'raid'. */
async function notes(env, me) {
  const { results } = await env.DB.prepare('SELECT id, kind, game, created_at FROM notes WHERE to_id = ?1 AND read = 0 ORDER BY id DESC LIMIT 20').bind(me.id).all();
  return json({ notes: (results || []).map((n) => ({ ...n, game: parse(n.game) })) });
}

/** {ids}: marks those notes read. */
async function notesRead(env, me, body) {
  const ids = (Array.isArray(body.ids) ? body.ids : []).slice(0, 50).map((i) => parseInt(i, 10)).filter(Number.isInteger);
  await env.DB.prepare('UPDATE notes SET read = 1 WHERE to_id = ?1 AND id IN (SELECT value FROM json_each(?2))').bind(me.id, JSON.stringify(ids)).run();
  return json({ ok: true });
}

/** Statements that delete a player's tales, raid hits, notes and daily rows (for a batch that deletes the account). */
export const deleteQuestRows = (env, pid) => [
  env.DB.prepare('DELETE FROM quest_runs WHERE owner_id = ?1').bind(pid),
  env.DB.prepare('DELETE FROM quest_raid_hits WHERE player_id = ?1').bind(pid),
  env.DB.prepare('DELETE FROM notes WHERE to_id = ?1 OR from_id = ?1').bind(pid),
  env.DB.prepare('DELETE FROM quest_daily WHERE player_id = ?1').bind(pid),
];

// ---- cleaning what clients send ----

/**
 * A hero snapshot (the game's HeroSeed as JSON) that other players' games will build a pet from: word keys, finite
 * numbers, short strings without control characters or angle brackets, lists of 24 and objects of 40 at most, three
 * levels deep. The cleaned object, or null when it isn't an object or comes to more than max characters.
 */
export function cleanSeed(v, max) {
  const clean = (x, depth) => {
    if (typeof x === 'number') return Number.isFinite(x) ? Math.max(-1e9, Math.min(1e9, x)) : 0;
    if (typeof x === 'boolean') return x;
    if (typeof x === 'string') return x.replace(/[\u0000-\u001f<>]/g, '').slice(0, 600);
    if (!x || typeof x !== 'object' || depth > 3) return null;
    if (Array.isArray(x)) return x.slice(0, 24).map((y) => clean(y, depth + 1));
    const o = {};
    for (const [k, y] of Object.entries(x).slice(0, 40)) if (/^\w{1,16}$/.test(k)) o[k] = clean(y, depth + 1);
    return o;
  };
  if (!v || typeof v !== 'object' || Array.isArray(v)) return null;
  const out = clean(v, 1);
  return JSON.stringify(out).length <= max ? out : null;
}

// a pet look (pets.js cleanPet) as an object, or null
const look = (v) => { const t = cleanPet(v); return t ? JSON.parse(t) : null; };

const int = (v, lo, hi, d) => Math.max(lo, Math.min(hi, parseInt(v, 10) || d));
const parse = (s) => { try { return JSON.parse(s); } catch { return null; } };
const b64url = (bytes) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

// a JSON body of at most 420 000 characters (a saved tale's state may be 380 000)
async function readBody(request) {
  const text = await request.text();
  if (text.length > 420000) throw new Problem('Too big', 413);
  const o = parse(text);
  if (!o || typeof o !== 'object' || Array.isArray(o)) throw new Problem('Bad request');
  return o;
}
