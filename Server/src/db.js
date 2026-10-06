// Shared by the routes: the D1 database (tables made automatically on first use), daily limits, the
// blocked-words list, the towns and rooms, and the small reply helpers.

/** Creates the tables on first use, so a fresh D1 database works without a separate step. */
let schemaReady = false;
export async function ensureSchema(env) {
  if (schemaReady) return;
  await env.DB.batch(SCHEMA.map((sql) => env.DB.prepare(sql)));
  schemaReady = true;
}

const SCHEMA = [
  `CREATE TABLE IF NOT EXISTS players (id TEXT PRIMARY KEY, name TEXT NOT NULL, pet TEXT, recovery_code TEXT UNIQUE,
     coins INTEGER NOT NULL DEFAULT 0, is_admin INTEGER NOT NULL DEFAULT 0, mute_until INTEGER NOT NULL DEFAULT 0,
     ban_until INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL, last_seen INTEGER)`,
  `CREATE TABLE IF NOT EXISTS tokens (hash TEXT PRIMARY KEY, player_id TEXT NOT NULL, created_at INTEGER NOT NULL)`,
  `CREATE INDEX IF NOT EXISTS tokens_player ON tokens (player_id)`,
  `CREATE TABLE IF NOT EXISTS finds (player_id TEXT NOT NULL, day TEXT NOT NULL, n INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (player_id, day))`,
  `CREATE TABLE IF NOT EXISTS limits (kind TEXT NOT NULL, ip TEXT NOT NULL, day TEXT NOT NULL, n INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (kind, ip, day))`,
  `CREATE TABLE IF NOT EXISTS blocked_words (word TEXT PRIMARY KEY)`,
  `CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT NOT NULL)`,
  `CREATE TABLE IF NOT EXISTS admin_log (id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL, admin_id TEXT NOT NULL,
     admin_name TEXT NOT NULL, action TEXT NOT NULL, target_id TEXT, target_name TEXT, detail TEXT)`,
  `CREATE INDEX IF NOT EXISTS admin_log_target ON admin_log (target_id)`,
  `CREATE TABLE IF NOT EXISTS pets (player_id TEXT NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, look TEXT NOT NULL,
     active INTEGER NOT NULL DEFAULT 0, born INTEGER NOT NULL, PRIMARY KEY (player_id, id))`,
];

export const today = () => new Date().toISOString().slice(0, 10);

export async function countToday(env, kind, ip) {
  const row = await env.DB.prepare('SELECT n FROM limits WHERE kind = ?1 AND ip = ?2 AND day = ?3').bind(kind, ip, today()).first();
  return row ? row.n : 0;
}

export const bumpToday = (env, kind, ip) =>
  env.DB.prepare('INSERT INTO limits (kind, ip, day, n) VALUES (?1, ?2, ?3, 1) ON CONFLICT (kind, ip, day) DO UPDATE SET n = n + 1').bind(kind, ip, today()).run();

/** Extra words you've blocked in D1 (cached for a minute). */
let blocked = { at: 0, words: [] };
export async function blockedWords(env) {
  if (Date.now() - blocked.at < 60e3) return blocked.words;
  const { results } = await env.DB.prepare('SELECT word FROM blocked_words').all();
  blocked = { at: Date.now(), words: (results || []).map((r) => r.word) };
  return blocked.words;
}

// ---- towns and rooms ----

/** The live towns. Each has rooms 1 to ROOMS, and each room is one Durable Object named "<town>:<room>". */
export const TOWNS = ['pawtopia', 'road1', 'caves'];
export const ROOMS = 6;

// ---- replies ----

/** An error the player should see, with its HTTP status. */
export class Problem extends Error {
  constructor(message, status = 400) { super(message); this.status = status; }
}

export async function readJson(request) {
  try { const o = await request.json(); return o && typeof o === 'object' ? o : {}; } catch { return {}; }
}

export const json = (o, status = 200) => new Response(JSON.stringify(o), { status, headers: { 'content-type': 'application/json' } });
