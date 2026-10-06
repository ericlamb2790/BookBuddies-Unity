// A player's pets, up to six like the website: each has an id, a name and a look, and one of them is active.
// The active pet's look is also players.pet, which sign-in, the town and every older route already use.
// The first pet is made from players.pet the first time anything asks for the list.

import { textProblem, cleanText } from './safety.js';
import { blockedWords, Problem, json, readJson } from './db.js';

export const MAX_PETS = 6;

// The website's pet look (cleanPet, build 551): hue h 0-359, stage s 0-5, outfit o {slot: item id}, body, face, ears,
// pattern, background, mane, tail and skin ids, size z 0-2, rank r, shiny sy, evolution form ev 1-3, mood m, state st, name n.
const PET_SLOTS = ['hat', 'eye', 'top', 'dress', 'bottom', 'shoes', 'neck', 'hand'];
const PET_LOOKS = ['sh', 'f', 'e', 'pt', 'bg', 'mn', 'tl', 'sk'];
const PART_ID = /^[a-z0-9_]{1,16}$/;
const int = (v, lo, hi, d = 0) => Math.max(lo, Math.min(hi, parseInt(v, 10) || d));

/** A pet look as tidy JSON text (known keys and simple ids only, at most 500 characters), or null when it can't be read. */
export function cleanPet(v) {
  try {
    const p = typeof v === 'string' ? JSON.parse(v) : v;
    if (!p || typeof p !== 'object' || Array.isArray(p)) return null;
    const out = { h: int(p.h, 0, 359), s: int(p.s, 0, 5), o: {} };
    for (const k of PET_SLOTS) { const id = p.o && p.o[k]; if (typeof id === 'string' && PART_ID.test(id)) out.o[k] = id; }
    for (const k of PET_LOOKS) if (typeof p[k] === 'string' && PART_ID.test(p[k])) out[k] = p[k];
    if (p.z !== undefined) out.z = int(p.z, 0, 2);
    if (p.r !== undefined) out.r = int(p.r, 0, 9999);
    if (p.sy) out.sy = 1;
    if (p.ev !== undefined) out.ev = int(p.ev, 1, 3, 1);
    if (typeof p.m === 'string' && /^[a-z]{1,12}$/.test(p.m)) out.m = p.m;
    if (p.st !== undefined) out.st = int(p.st, 0, 6);
    if (typeof p.n === 'string') { const n = p.n.replace(/[<>"&\u0000-\u001f]/g, '').trim().slice(0, 14); if (n) out.n = n; }
    const text = JSON.stringify(out);
    return text.length <= 500 ? text : null;
  } catch { return null; }
}

/** GET, POST /me/pets, PATCH /me/pets/<id> and POST /me/pets/<id>/active. Every reply is the whole list. */
export async function petsRoute(request, env, path, me) {
  const method = request.method;
  if (path === '/me/pets' && method === 'GET') return reply(env, me);
  if (path === '/me/pets' && method === 'POST') return hatch(request, env, me);
  const m = path.match(/^\/me\/pets\/([a-z0-9]{1,24})(\/active)?$/);
  if (!m || method !== (m[2] ? 'POST' : 'PATCH')) throw new Problem('Not found', 404);
  await firstPet(env, me);
  const pet = await env.DB.prepare('SELECT * FROM pets WHERE player_id = ?1 AND id = ?2').bind(me.id, m[1]).first();
  if (!pet) throw new Problem('That pet isn’t one of yours.', 404);
  return m[2] ? makeActive(env, me, pet) : change(request, env, me, pet);
}

/** The player's pets, oldest first, and the active one's id: {pets: [{id, name, look}], active}. */
export async function petList(env, me) {
  await firstPet(env, me);
  const { results } = await env.DB.prepare('SELECT id, name, look, active FROM pets WHERE player_id = ?1 ORDER BY born, id').bind(me.id).all();
  const rows = results || [];
  return { pets: rows.map(({ id, name, look }) => ({ id, name, look })), active: (rows.find((r) => r.active) || {}).id || '' };
}

/** Keeps the active pet's look in step when an older client changes players.pet with PATCH /me. */
export const activeLook = (env, me, look) => env.DB.prepare('UPDATE pets SET look = ?2 WHERE player_id = ?1 AND active = 1').bind(me.id, look);

// the pet you hatched with becomes the first on the list (once; nothing happens when the list has pets already)
function firstPet(env, me) {
  if (!me.pet) return null;
  return env.DB.prepare('INSERT INTO pets (player_id, id, name, look, active, born) SELECT ?1, \'p1\', ?2, ?3, 1, ?4 WHERE NOT EXISTS (SELECT 1 FROM pets WHERE player_id = ?1)')
    .bind(me.id, me.name, me.pet, me.created_at || Date.now()).run();
}

// a new pet from an egg; it becomes the active one, like hatching on the website
async function hatch(request, env, me) {
  const body = await readJson(request);
  const name = await petName(env, body.name);
  const look = cleanPet(body.look);
  if (!look) throw new Problem('That pet look couldn’t be read.');
  const { pets } = await petList(env, me);
  if (pets.length >= MAX_PETS) throw new Problem(`You have ${MAX_PETS} pets already. That’s a full nest!`);
  const id = 'p' + [...crypto.getRandomValues(new Uint8Array(5))].map((b) => b.toString(16).padStart(2, '0')).join('');
  await env.DB.batch([
    env.DB.prepare('UPDATE pets SET active = 0 WHERE player_id = ?1').bind(me.id),
    env.DB.prepare('INSERT INTO pets (player_id, id, name, look, active, born) VALUES (?1, ?2, ?3, ?4, 1, ?5)').bind(me.id, id, name, look, Date.now()),
    env.DB.prepare('UPDATE players SET pet = ?2 WHERE id = ?1').bind(me.id, look),
  ]);
  me.pet = look;
  return reply(env, me);
}

// a DNA reroll (a new look) or a new name
async function change(request, env, me, pet) {
  const body = await readJson(request);
  if (body.name !== undefined) pet.name = await petName(env, body.name);
  if (body.look !== undefined) {
    pet.look = cleanPet(body.look);
    if (!pet.look) throw new Problem('That pet look couldn’t be read.');
  }
  const steps = [env.DB.prepare('UPDATE pets SET name = ?3, look = ?4 WHERE player_id = ?1 AND id = ?2').bind(me.id, pet.id, pet.name, pet.look)];
  if (pet.active) { steps.push(env.DB.prepare('UPDATE players SET pet = ?2 WHERE id = ?1').bind(me.id, pet.look)); me.pet = pet.look; }
  await env.DB.batch(steps);
  return reply(env, me);
}

async function makeActive(env, me, pet) {
  await env.DB.batch([
    env.DB.prepare('UPDATE pets SET active = (id = ?2) WHERE player_id = ?1').bind(me.id, pet.id),
    env.DB.prepare('UPDATE players SET pet = ?2 WHERE id = ?1').bind(me.id, pet.look),
  ]);
  me.pet = pet.look;
  return reply(env, me);
}

async function petName(env, raw) {
  const name = cleanText(raw, 14);
  if (name.length < 2) throw new Problem('Pick a name with at least 2 letters.');
  if (textProblem(name, await blockedWords(env))) throw new Problem('Please pick a kinder name.');
  return name;
}

const reply = async (env, me) => json({ ...(await petList(env, me)), pet: me.pet || '' });
