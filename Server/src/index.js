// BookBuddies Unity server: accounts, pets, the wallet, the live town and Tales of Pages, on one Cloudflare Worker.
// D1 keeps players and sign-ins; each live town room is a Durable Object (see town.js).
// The routes and messages match the website's, so the Unity game can talk to either server.

import { textProblem, cleanText } from './safety.js';
import { ensureSchema, blockedWords, countToday, bumpToday, TOWNS, ROOMS, noteRoom, forgetRooms, Problem, json, readJson } from './db.js';
import { adminRoute, breakMessage } from './admin.js';
import { cleanPet, petsRoute, petList, activeLook } from './pets.js';
import { walletRoute, balance, deleteWalletRows } from './wallet.js';
import { questRoute, openRun, deleteQuestRows } from './quest.js';
export { TownRoom } from './town.js';

const VERSION = '0.3';
const PASS_MINUTES = 15;         // a pass lets you rejoin quickly without asking for a new ticket
const SIGNUPS_PER_IP_PER_DAY = 10;
const WRONG_CODES_PER_IP_PER_DAY = 15;

export default {
  async fetch(request, env) {
    if (request.method === 'OPTIONS') return cors(new Response(null, { status: 204 }), env);
    const url = new URL(request.url);
    if (url.pathname === '/api/world/live') return joinTown(request, env, url);
    if (url.pathname === '/api/quest/live') return joinTale(request, env, url);
    try {
      await ensureSchema(env);
      return cors(await route(request, env, url), env);
    } catch (e) {
      if (e instanceof Problem) return cors(json({ error: e.message }, e.status), env);
      console.error(e);
      return cors(json({ error: 'Something went wrong on the server. Please try again.' }, 500), env);
    }
  },
};

async function route(request, env, url) {
  const path = url.pathname.replace(/^\/api/, '');
  const method = request.method;

  if (path === '/health') return json({ ok: true, server: 'bookbuddies-unity', version: VERSION, accounts: true });
  if (path === '/register' && method === 'POST') return register(request, env);
  if (path === '/link/claim' && method === 'POST') return signIn(request, env);

  const me = await signedIn(request, env);
  if (path === '/me' && method === 'GET') return json({ ...account(me), coins: await balance(env, me.id), ...(await petList(env, me)) });
  if (path === '/me' && method === 'PATCH') return updateMe(request, env, me);
  if (path === '/me/recovery' && method === 'GET') return json({ code: formatCode(me.recovery_code) });
  if (path === '/me/delete' && method === 'POST') return deleteMe(env, me);
  if (path === '/me/pets' || path.startsWith('/me/pets/')) return petsRoute(request, env, path, me);
  if (path === '/plaza/world/ticket' && method === 'GET') return townTicket(env, me, url);
  if (path === '/wallet' || path.startsWith('/wallet/')) return walletRoute(request, env, path, me);
  if (path.startsWith('/admin/')) return adminRoute(request, env, path, url, me);
  if (path === '/quest/ticket' && method === 'GET') return taleTicket(env, me, url);
  if (path.startsWith('/quest/') || path === '/notes' || path === '/notes/read') return questRoute(request, env, path, url, me);
  throw new Problem('Not found', 404);
}

// ---------- accounts ----------

/** A new reader hatches their egg: makes the account and its recovery code. */
async function register(request, env) {
  const body = await readJson(request);
  const name = cleanText(body.name, 20);
  if (name.length < 2) throw new Problem('Pick a name with at least 2 letters.');
  if (textProblem(name, await blockedWords(env))) throw new Problem('Please pick a kinder name.');
  const pet = cleanPet(body.pet);
  if (!pet) throw new Problem('Your pet’s look is missing.');

  const ip = request.headers.get('cf-connecting-ip') || 'local';
  if (await countToday(env, 'signups', ip) >= SIGNUPS_PER_IP_PER_DAY) throw new Problem('Lots of new pets from here today. Try again tomorrow.', 429);
  await bumpToday(env, 'signups', ip);

  const id = crypto.randomUUID();
  const code = newRecoveryCode();
  await env.DB.prepare('INSERT INTO players (id, name, pet, recovery_code, created_at, last_seen) VALUES (?1, ?2, ?3, ?4, ?5, ?5)')
    .bind(id, name, pet, code, Date.now()).run();
  const token = await newToken(env, id);
  return json({ token, recovery: formatCode(code), id, name, pet, is_admin: false });
}

/** Sign in with a recovery code (BB-XXXXX-XXXXX). Wrong guesses are limited per day; players on a break wait it out. */
async function signIn(request, env) {
  const ip = request.headers.get('cf-connecting-ip') || 'local';
  if (await countToday(env, 'wrong_codes', ip) >= WRONG_CODES_PER_IP_PER_DAY) throw new Problem('Too many wrong codes today. Try again tomorrow.', 429);
  const code = cleanCode((await readJson(request)).code);
  const player = code && await env.DB.prepare('SELECT * FROM players WHERE recovery_code = ?1').bind(code).first();
  if (!player) {
    await bumpToday(env, 'wrong_codes', ip);
    throw new Problem('That code didn’t match an account. Check it and try again.', 404);
  }
  if (player.ban_until > Date.now()) throw new Problem(breakMessage(player.ban_until), 403);
  const token = await newToken(env, player.id);
  return json({ token, ...account(player) });
}

async function updateMe(request, env, me) {
  const body = await readJson(request);
  if (body.name !== undefined) {
    const name = cleanText(body.name, 20);
    if (name.length < 2) throw new Problem('Pick a name with at least 2 letters.');
    if (textProblem(name, await blockedWords(env))) throw new Problem('Please pick a kinder name.');
    me.name = name;
  }
  if (body.pet !== undefined) {
    const pet = cleanPet(body.pet);
    if (!pet) throw new Problem('That pet look couldn’t be read.');
    me.pet = pet;
  }
  await env.DB.batch([
    env.DB.prepare('UPDATE players SET name = ?2, pet = ?3 WHERE id = ?1').bind(me.id, me.name, me.pet),
    ...(body.pet !== undefined ? [activeLook(env, me, me.pet)] : []),
  ]);
  return json(account(me));
}

async function deleteMe(env, me) {
  await env.DB.batch([
    env.DB.prepare('DELETE FROM tokens WHERE player_id = ?1').bind(me.id),
    ...deleteWalletRows(env, me.id),
    ...deleteQuestRows(env, me.id),
    env.DB.prepare('DELETE FROM pets WHERE player_id = ?1').bind(me.id),
    forgetRooms(env, me.id),
    env.DB.prepare('DELETE FROM players WHERE id = ?1').bind(me.id),
  ]);
  return json({ ok: true });
}

const account = (p) => ({ id: p.id, name: p.name, pet: p.pet || '', is_admin: !!p.is_admin });

async function signedIn(request, env) {
  const m = (request.headers.get('authorization') || '').match(/^Bearer\s+([a-f0-9]{64})$/i);
  if (!m) throw new Problem('Please sign in.', 401);
  const player = await env.DB.prepare('SELECT p.* FROM tokens t JOIN players p ON p.id = t.player_id WHERE t.hash = ?1').bind(await sha256(m[1].toLowerCase())).first();
  if (!player) throw new Problem('Please sign in again.', 401);
  return player;
}

async function newToken(env, playerId) {
  const token = hex(crypto.getRandomValues(new Uint8Array(32)));
  await env.DB.prepare('INSERT INTO tokens (hash, player_id, created_at) VALUES (?1, ?2, ?3)').bind(await sha256(token), playerId, Date.now()).run();
  return token;
}

// Recovery codes: "BB" plus 10 letters and digits that are hard to mix up (no 0/O, 1/I/L).
const CODE_ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
function newRecoveryCode() {
  const bytes = crypto.getRandomValues(new Uint8Array(10));
  return 'BB' + [...bytes].map((b) => CODE_ALPHABET[b % CODE_ALPHABET.length]).join('');
}
const cleanCode = (c) => { const s = String(c || '').toUpperCase().replace(/[^A-Z0-9]/g, ''); return s.length === 12 && s.startsWith('BB') ? s : null; };
const formatCode = (c) => (c ? `${c.slice(0, 2)}-${c.slice(2, 7)}-${c.slice(7)}` : '');

// ---------- the live town ----------

/** A short-lived signed ticket for room 1-6 of a place (?town=pawtopia, romance, road2, caves… see TOWNS), plus a 15-minute pass for quick rejoins. */
async function townTicket(env, me, url) {
  if (me.ban_until > Date.now()) return json({ live: false, reason: 'break', msg: breakMessage(me.ban_until) });
  const shard = Math.max(1, Math.min(ROOMS, parseInt(url.searchParams.get('s'), 10) || 1));
  const asked = url.searchParams.get('town');
  const town = TOWNS.includes(asked) ? asked : 'pawtopia';
  const name = encodeURIComponent(me.name);
  const ticket = await sign(env, `${me.id}|${Date.now() + 60e3}|${name}|${town}:${shard}`);
  const passExp = Date.now() + PASS_MINUTES * 60e3;
  const pass = await sign(env, `${me.id}|${passExp}|${name}|${town}:*`);
  env.DB.prepare('UPDATE players SET last_seen = ?2 WHERE id = ?1').bind(me.id, Date.now()).run().catch(() => {});
  return json({ live: true, shard, town, ticket, pass, passExp });
}

/** Opens the WebSocket into the town room named on the ticket. */
async function joinTown(request, env, url) {
  if (request.headers.get('Upgrade') !== 'websocket') return new Response('Expected a WebSocket', { status: 426 });
  await ensureSchema(env);
  const who = await checkTicket(env, url.searchParams.get('ticket'));
  if (!who || who.room.startsWith('qs:')) return new Response('Bad or old ticket', { status: 401 });
  let room = who.room;
  if (room.endsWith(':*')) room = `${room.slice(0, -2)}:${Math.max(1, Math.min(ROOMS, parseInt(url.searchParams.get('s'), 10) || 1))}`;
  await noteRoom(env, who.pid, room);
  const headers = new Headers(request.headers);
  headers.set('x-bb-pid', who.pid);
  headers.set('x-bb-name', encodeURIComponent(who.name));
  headers.delete('x-bb-kind');
  return env.TOWNS.get(env.TOWNS.idFromName(room)).fetch(new Request(request.url, { headers }));
}

// ---------- a shared tale's live party (quest.js, tales_room.js) ----------

/** A 60-second ticket into a shared tale's party room "qs:<id>" for anyone who may open the tale; {live: false} otherwise. */
async function taleTicket(env, me, url) {
  const q = me.ban_until > Date.now() ? null : await openRun(env, me, url.searchParams.get('id'));
  if (!q) return json({ live: false });
  const ticket = await sign(env, `${me.id}|${Date.now() + 60e3}|${encodeURIComponent(me.name)}|qs:${q.id}`);
  return json({ live: true, ticket, me: me.id });
}

/** Opens the WebSocket into the tale's party room named on the ticket (a TownRoom that hands it to tales_room.js). */
async function joinTale(request, env, url) {
  if (request.headers.get('Upgrade') !== 'websocket') return new Response('Expected a WebSocket', { status: 426 });
  await ensureSchema(env);
  const who = await checkTicket(env, url.searchParams.get('ticket'));
  if (!who || !who.room.startsWith('qs:')) return new Response('Bad or old ticket', { status: 401 });
  const headers = new Headers(request.headers);
  headers.set('x-bb-pid', who.pid);
  headers.set('x-bb-name', encodeURIComponent(who.name));
  headers.set('x-bb-kind', 'quest');
  return env.TOWNS.get(env.TOWNS.idFromName(who.room)).fetch(new Request(request.url, { headers }));
}

async function sign(env, body) {
  const sig = await hmac(await ticketKey(env), body);
  return b64url(new TextEncoder().encode(body)) + '.' + sig;
}

async function checkTicket(env, ticket) {
  const [body64, sig] = String(ticket || '').split('.');
  if (!body64 || !sig) return null;
  let body;
  try { body = new TextDecoder().decode(unb64url(body64)); } catch { return null; }
  if (await hmac(await ticketKey(env), body) !== sig) return null;
  const [pid, exp, name, room] = body.split('|');
  if (!pid || !(+exp > Date.now()) || !room) return null;
  return { pid, name: decodeURIComponent(name || 'Reader'), room };
}

/** The ticket-signing key: the TICKET_SECRET secret if you set one, otherwise a random key saved in D1 on first use. */
let cachedKey = null;
async function ticketKey(env) {
  if (cachedKey) return cachedKey;
  let secret = env.TICKET_SECRET;
  if (!secret) {
    const row = await env.DB.prepare("SELECT v FROM meta WHERE k = 'ticket_key'").first();
    secret = row ? row.v : hex(crypto.getRandomValues(new Uint8Array(32)));
    if (!row) await env.DB.prepare("INSERT OR IGNORE INTO meta (k, v) VALUES ('ticket_key', ?1)").bind(secret).run();
    if (!row) secret = (await env.DB.prepare("SELECT v FROM meta WHERE k = 'ticket_key'").first()).v;
  }
  cachedKey = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  return cachedKey;
}

// ---------- small helpers ----------

function cors(res, env) {
  const h = new Headers(res.headers);
  h.set('access-control-allow-origin', env.ALLOWED_ORIGIN || '*');
  h.set('access-control-allow-methods', 'GET, POST, PATCH, OPTIONS');
  h.set('access-control-allow-headers', 'authorization, content-type');
  return new Response(res.body, { status: res.status, headers: h });
}

const hex = (bytes) => [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('');
const sha256 = async (text) => hex(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))));
const hmac = async (key, text) => b64url(new Uint8Array(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(text))));
const b64url = (bytes) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const unb64url = (s) => Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/')), (c) => c.charCodeAt(0));
