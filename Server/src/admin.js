// Admin tools for accounts: find players, pause their chat, give them a break from town, send them home,
// rename them, set their coins, share admin rights and delete accounts. Every route needs an admin
// account, and every change is written to the admin_log table so other admins can see who did what.

import { textProblem, cleanText } from './safety.js';
import { blockedWords, TOWNS, ROOMS, Problem, json, readJson } from './db.js';

const FOREVER = 8.64e15;              // a break "for good" lasts until the last date JavaScript can hold
const MAX_MUTE_MINUTES = 30 * 24 * 60;
const MAX_BREAK_HOURS = 365 * 24;
const MAX_COINS = 1e9;
const KEEP_LOG = 5000;                // older admin_log rows are trimmed

/** Routes under /api/admin. "me" is the signed-in player; only admins get past the first line. */
export async function adminRoute(request, env, path, url, me) {
  if (!me.is_admin) throw new Problem('Only town admins can do that.', 403);
  const method = request.method;
  if (path === '/admin/players' && method === 'GET') return reply(await search(env, url));
  if (path === '/admin/log' && method === 'GET') return reply({ log: await recentLog(env, null, limitOf(url, 60)) });

  const m = path.match(/^\/admin\/players\/([\w-]{1,64})(?:\/([a-z]+))?$/);
  if (!m) throw new Problem('Not found', 404);
  const player = await env.DB.prepare('SELECT * FROM players WHERE id = ?1').bind(m[1]).first();
  if (!player) throw new Problem('That player doesn’t exist anymore.', 404);
  if (!m[2] && method === 'GET') return reply({ player: view(player), log: await recentLog(env, player.id, 20) });

  const action = ACTIONS[m[2]];
  if (!action || method !== 'POST') throw new Problem('Not found', 404);
  const before = player.name;
  const detail = await action(env, me, player, await readJson(request));
  await writeLog(env, me, m[2], player.id, before, detail);
  return reply({ player: m[2] === 'delete' ? null : view(player), log: await recentLog(env, player.id, 20) });
}

/** The message a player on a break sees when they try to sign in or join town. */
export function breakMessage(until) {
  if (until >= FOREVER) return 'An admin closed this account’s visits to town.';
  const mins = Math.max(1, Math.ceil((until - Date.now()) / 60e3));
  return `An admin asked you to take a break from town. You can come back in ${duration(mins)}.`;
}

// ---- actions: each changes the player, tells the live rooms, and returns a few words for the log ----

const ACTIONS = {
  async mute(env, me, p, body) {
    protect(me, p);
    const minutes = whole(body.minutes, 1, MAX_MUTE_MINUTES);
    p.mute_until = Date.now() + minutes * 60e3;
    await save(env, p, 'mute_until');
    await tellRooms(env, { action: 'mute', pid: p.id, until: p.mute_until });
    return `for ${duration(minutes)}`;
  },

  async unmute(env, me, p) {
    p.mute_until = 0;
    await save(env, p, 'mute_until');
    await tellRooms(env, { action: 'unmute', pid: p.id });
    return '';
  },

  /** A break from town: { hours } or { permanent: true }. They're sent home right away. */
  async ban(env, me, p, body) {
    protect(me, p);
    const hours = body.permanent ? 0 : whole(body.hours, 1, MAX_BREAK_HOURS);
    p.ban_until = hours ? Date.now() + hours * 3600e3 : FOREVER;
    await save(env, p, 'ban_until');
    await tellRooms(env, { action: 'kick', pid: p.id, msg: breakMessage(p.ban_until) });
    return hours ? `for ${duration(hours * 60)}` : 'for good';
  },

  async unban(env, me, p) {
    p.ban_until = 0;
    await save(env, p, 'ban_until');
    return '';
  },

  /** Sends them home from town now; they can come back whenever they like. */
  async kick(env, me, p) {
    protect(me, p);
    await tellRooms(env, { action: 'kick', pid: p.id, msg: 'An admin sent you home from town for now. 💛' });
    return '';
  },

  async rename(env, me, p, body) {
    const name = cleanText(body.name, 20);
    if (name.length < 2) throw new Problem('Pick a name with at least 2 letters.');
    if (textProblem(name, await blockedWords(env))) throw new Problem('Please pick a kinder name.');
    p.name = name;
    await save(env, p, 'name');
    return `to “${name}”`;
  },

  async coins(env, me, p, body) {
    const coins = whole(body.coins, 0, MAX_COINS);
    const old = p.coins;
    p.coins = coins;
    await save(env, p, 'coins');
    return `from ${old} to ${coins}`;
  },

  /** { on: true } shares admin rights, { on: false } takes them back. Never your own. */
  async admin(env, me, p, body) {
    if (p.id === me.id) throw new Problem('You can’t change your own admin rights.');
    p.is_admin = body.on ? 1 : 0;
    await save(env, p, 'is_admin');
    return p.is_admin ? 'on' : 'off';
  },

  async delete(env, me, p) {
    if (p.id === me.id) throw new Problem('Delete your own account from Settings → Account.');
    protect(me, p);
    await env.DB.batch([
      env.DB.prepare('DELETE FROM tokens WHERE player_id = ?1').bind(p.id),
      env.DB.prepare('DELETE FROM finds WHERE player_id = ?1').bind(p.id),
      env.DB.prepare('DELETE FROM pets WHERE player_id = ?1').bind(p.id),
      env.DB.prepare('DELETE FROM players WHERE id = ?1').bind(p.id),
    ]);
    await tellRooms(env, { action: 'kick', pid: p.id, msg: 'This account was closed by an admin.' });
    return '';
  },
};

/** Admins can't mute, pause, kick or delete themselves or each other (take the rights away first). */
function protect(me, p) {
  if (p.id === me.id) throw new Problem('You can’t do that to your own account.');
  if (p.is_admin) throw new Problem('They’re an admin. Take their admin rights away first.');
}

const COLUMNS = new Set(['name', 'coins', 'is_admin', 'mute_until', 'ban_until']);
const save = (env, p, column) => {
  if (!COLUMNS.has(column)) throw new Error('unknown column ' + column);
  return env.DB.prepare(`UPDATE players SET ${column} = ?2 WHERE id = ?1`).bind(p.id, p[column]).run();
};

/** Passes a live change (kick, mute, unmute) to every room of every town, so it reaches the player wherever they are. */
async function tellRooms(env, message) {
  const body = JSON.stringify(message);
  const calls = [];
  for (const town of TOWNS) {
    for (let room = 1; room <= ROOMS; room++) {
      const stub = env.TOWNS.get(env.TOWNS.idFromName(`${town}:${room}`));
      calls.push(stub.fetch('https://room/admin', { method: 'POST', headers: { 'content-type': 'application/json' }, body }));
    }
  }
  await Promise.allSettled(calls);
}

// ---- finding players ----

/** ?q= part of a name (or a whole id), ?exact=1 for the whole name; newest visitors first. */
async function search(env, url) {
  const q = (url.searchParams.get('q') || '').trim().slice(0, 64);
  const exact = url.searchParams.get('exact') === '1';
  let where = '', args = [];
  if (q && exact) { where = 'WHERE name = ?1 COLLATE NOCASE OR id = ?1'; args = [q]; }
  else if (q) { where = "WHERE name LIKE ?1 ESCAPE '\\' OR id = ?2"; args = ['%' + q.replace(/[\\%_]/g, (c) => '\\' + c) + '%', q]; }
  const { results } = await env.DB.prepare(`SELECT * FROM players ${where} ORDER BY COALESCE(last_seen, created_at) DESC LIMIT ${limitOf(url, 40)}`)
    .bind(...args).all();
  const total = await env.DB.prepare('SELECT COUNT(*) AS n FROM players').first();
  return { players: (results || []).map(view), total: total ? total.n : 0 };
}

/** What admins see of a player (never their recovery code). */
const view = (p) => ({
  id: p.id, name: p.name, pet: p.pet || '', coins: p.coins || 0, is_admin: !!p.is_admin,
  created_at: p.created_at, last_seen: p.last_seen || 0, mute_until: p.mute_until || 0, ban_until: p.ban_until || 0,
});

// ---- the log ----

async function writeLog(env, me, action, targetId, targetName, detail) {
  await env.DB.batch([
    env.DB.prepare('INSERT INTO admin_log (at, admin_id, admin_name, action, target_id, target_name, detail) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)')
      .bind(Date.now(), me.id, me.name, action, targetId, targetName, detail || ''),
    env.DB.prepare('DELETE FROM admin_log WHERE id <= (SELECT MAX(id) FROM admin_log) - ?1').bind(KEEP_LOG),
  ]);
}

/** The newest admin actions, for everyone or for one player. */
async function recentLog(env, playerId, limit) {
  const stmt = playerId
    ? env.DB.prepare(`SELECT * FROM admin_log WHERE target_id = ?1 ORDER BY id DESC LIMIT ${limit}`).bind(playerId)
    : env.DB.prepare(`SELECT * FROM admin_log ORDER BY id DESC LIMIT ${limit}`);
  const { results } = await stmt.all();
  return (results || []).map((r) => ({ at: r.at, admin: r.admin_name, action: r.action, target: r.target_name || '', target_id: r.target_id || '', detail: r.detail || '' }));
}

// ---- small helpers ----

/** Every admin reply carries the server's clock, so "muted for 5 more min" is right on any device. */
const reply = (o) => json({ ...o, now: Date.now() });

const limitOf = (url, fallback) => Math.max(1, Math.min(100, parseInt(url.searchParams.get('limit'), 10) || fallback));

function whole(v, min, max) {
  const n = Math.floor(Number(v));
  if (!Number.isFinite(n) || n < min) throw new Problem(`Pick a number from ${min} to ${max}.`);
  return Math.min(n, max);
}

function duration(minutes) {
  if (minutes < 60) return `${minutes} min`;
  if (minutes < 48 * 60) { const h = Math.round(minutes / 60); return `${h} hour${h === 1 ? '' : 's'}`; }
  const d = Math.round(minutes / 1440);
  return `${d} days`;
}
