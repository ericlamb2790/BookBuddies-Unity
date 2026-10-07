// Shared by the routes: the D1 database (tables made automatically on first use), daily limits, the
// blocked-words list, the towns and rooms, and the small reply helpers.

/** Creates the tables on first use, so a fresh D1 database works without a separate step, then runs the one-time moves. */
let schemaReady = false;
export async function ensureSchema(env) {
  if (schemaReady) return;
  await env.DB.batch(SCHEMA.map((sql) => env.DB.prepare(sql)));
  await moveCoinsToLedger(env);
  schemaReady = true;
}

/**
 * v0.4: coins used to be a number on the player (players.coins). They move into the coin ledger once, as one 'legacy'
 * row each (INSERT OR IGNORE, so a second run pays nothing), and the old per-day finds counter goes with them.
 */
async function moveCoinsToLedger(env) {
  if (await env.DB.prepare("SELECT 1 FROM meta WHERE k = 'wallet_v1'").first()) return;
  await env.DB.batch([
    env.DB.prepare("INSERT OR IGNORE INTO coin_tx (player_id, kind, ref, amount, day, created_at) SELECT id, 'legacy', 'once', coins, ?1, ?2 FROM players WHERE coins > 0")
      .bind(etDay(), Date.now()),
    env.DB.prepare('DROP TABLE IF EXISTS finds'),
    env.DB.prepare("INSERT OR IGNORE INTO meta (k, v) VALUES ('wallet_v1', ?1)").bind(String(Date.now())),
  ]);
}

const SCHEMA = [
  `CREATE TABLE IF NOT EXISTS players (id TEXT PRIMARY KEY, name TEXT NOT NULL, pet TEXT, recovery_code TEXT UNIQUE,
     coins INTEGER NOT NULL DEFAULT 0, is_admin INTEGER NOT NULL DEFAULT 0, mute_until INTEGER NOT NULL DEFAULT 0,
     ban_until INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL, last_seen INTEGER)`,
  `CREATE TABLE IF NOT EXISTS tokens (hash TEXT PRIMARY KEY, player_id TEXT NOT NULL, created_at INTEGER NOT NULL)`,
  `CREATE INDEX IF NOT EXISTS tokens_player ON tokens (player_id)`,
  `CREATE TABLE IF NOT EXISTS limits (kind TEXT NOT NULL, ip TEXT NOT NULL, day TEXT NOT NULL, n INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (kind, ip, day))`,
  `CREATE TABLE IF NOT EXISTS blocked_words (word TEXT PRIMARY KEY)`,
  `CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT NOT NULL)`,
  `CREATE TABLE IF NOT EXISTS admin_log (id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL, admin_id TEXT NOT NULL,
     admin_name TEXT NOT NULL, action TEXT NOT NULL, target_id TEXT, target_name TEXT, detail TEXT)`,
  `CREATE INDEX IF NOT EXISTS admin_log_target ON admin_log (target_id)`,
  `CREATE TABLE IF NOT EXISTS pets (player_id TEXT NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, look TEXT NOT NULL,
     active INTEGER NOT NULL DEFAULT 0, born INTEGER NOT NULL, PRIMARY KEY (player_id, id))`,
  // the wallet (wallet.js), the website's tables as they are: coins and Book Fair tickets as ledgers, the fair's counters
  `CREATE TABLE IF NOT EXISTS coin_tx (id INTEGER PRIMARY KEY AUTOINCREMENT, player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
     kind TEXT NOT NULL, ref TEXT NOT NULL, amount INTEGER NOT NULL, day TEXT NOT NULL, created_at INTEGER NOT NULL)`,
  `CREATE UNIQUE INDEX IF NOT EXISTS coin_tx_once_idx ON coin_tx (player_id, kind, ref)`,
  `CREATE INDEX IF NOT EXISTS coin_tx_day_idx ON coin_tx (player_id, day)`,
  `CREATE TABLE IF NOT EXISTS fair_tx (id INTEGER PRIMARY KEY AUTOINCREMENT, player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
     kind TEXT NOT NULL, ref TEXT NOT NULL, amount INTEGER NOT NULL, day TEXT NOT NULL, created_at INTEGER NOT NULL)`,
  `CREATE UNIQUE INDEX IF NOT EXISTS fair_tx_once_idx ON fair_tx (player_id, kind, ref)`,
  // coins banked from offline play (wallet.js bank): what each session banked (it banks once), and the finds banked per day
  `CREATE TABLE IF NOT EXISTS bank_sessions (player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE, id TEXT NOT NULL,
     banked INTEGER NOT NULL, refused INTEGER NOT NULL, created_at INTEGER NOT NULL, PRIMARY KEY (player_id, id))`,
  `CREATE TABLE IF NOT EXISTS bank_days (player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE, day TEXT NOT NULL,
     finds INTEGER NOT NULL, PRIMARY KEY (player_id, day))`,
  // the rooms each player joined lately: the admin tools tell only these (36 places × 6 rooms is too many to call)
  `CREATE TABLE IF NOT EXISTS rooms_seen (player_id TEXT NOT NULL, room TEXT NOT NULL, at INTEGER NOT NULL, PRIMARY KEY (player_id, room))`,
  `CREATE TABLE IF NOT EXISTS econ_state (player_id TEXT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE, pity INTEGER NOT NULL,
     balls INTEGER NOT NULL, tix INTEGER NOT NULL, drops INTEGER NOT NULL, fish_at INTEGER NOT NULL, ups TEXT NOT NULL, freecap TEXT)`,
  // Tales of Pages (quest.js): shared co-op tales, the Bramble Road raid and its hits, notes to players, the daily tale's board
  `CREATE TABLE IF NOT EXISTS quest_runs (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, title TEXT, chapter INTEGER NOT NULL DEFAULT 1, party TEXT,
     state TEXT, visibility TEXT NOT NULL DEFAULT 'link', over INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL,
     updated_at INTEGER NOT NULL, online INTEGER NOT NULL DEFAULT 0, pets TEXT, srev INTEGER NOT NULL DEFAULT 0, last_by TEXT, kind TEXT)`,
  `CREATE INDEX IF NOT EXISTS quest_runs_owner_idx ON quest_runs (owner_id, updated_at)`,
  `CREATE TABLE IF NOT EXISTS quest_raids (id TEXT PRIMARY KEY, club TEXT NOT NULL, n INTEGER NOT NULL, boss INTEGER NOT NULL, lvl INTEGER NOT NULL,
     hp INTEGER NOT NULL, max INTEGER NOT NULL, started_at INTEGER NOT NULL, ends_at INTEGER NOT NULL, done_at INTEGER)`,
  `CREATE INDEX IF NOT EXISTS quest_raids_club_idx ON quest_raids (club, started_at)`,
  `CREATE TABLE IF NOT EXISTS quest_raid_hits (raid_id TEXT NOT NULL, player_id TEXT NOT NULL, dmg INTEGER NOT NULL DEFAULT 0,
     tries INTEGER NOT NULL DEFAULT 0, day TEXT, pet TEXT, last_at INTEGER, PRIMARY KEY (raid_id, player_id))`,
  `CREATE TABLE IF NOT EXISTS notes (id INTEGER PRIMARY KEY AUTOINCREMENT, to_id TEXT NOT NULL, from_id TEXT NOT NULL, kind TEXT NOT NULL,
     game TEXT, day TEXT NOT NULL, created_at INTEGER NOT NULL, read INTEGER NOT NULL DEFAULT 0)`,
  `CREATE INDEX IF NOT EXISTS notes_to_idx ON notes (to_id, read)`,
  `CREATE UNIQUE INDEX IF NOT EXISTS notes_once_idx ON notes (from_id, to_id, kind, day, game)`,
  `CREATE TABLE IF NOT EXISTS quest_daily (day TEXT NOT NULL, player_id TEXT NOT NULL, score INTEGER NOT NULL DEFAULT 0, label TEXT, pet TEXT,
     look TEXT, state TEXT, over INTEGER NOT NULL DEFAULT 0, at INTEGER NOT NULL, PRIMARY KEY (day, player_id))`,
  `CREATE INDEX IF NOT EXISTS quest_daily_board_idx ON quest_daily (day, score)`,
];

export const today = () => new Date().toISOString().slice(0, 10);

/** Today in US Eastern time (YYYY-MM-DD), like the website: every coin cap and the daily gift turn over at its midnight. */
export const etDay = () =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());

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

/** The towns along Bramble Road after Pawtopia, in order. Road link l runs from the town before it to the l-th of these. */
export const GENRE_TOWNS = ['romance', 'classics', 'adventure', 'mystery', 'fantasy', 'scifi', 'horror', 'cozy', 'fairytale',
  'poetry', 'western', 'ocean', 'historical', 'thriller', 'dystopia', 'myth', 'gothic'];

/**
 * The live places: Pawtopia, the 17 towns, the 17 road links between them ("road1" … "road17") and the Inkwell Caves.
 * Each has rooms 1 to ROOMS, and each room is one Durable Object named "<town>:<room>".
 */
export const TOWNS = ['pawtopia', ...GENRE_TOWNS, ...GENRE_TOWNS.map((_, i) => 'road' + (i + 1)), 'caves'];
export const ROOMS = 6;

/** Notes the room a player just joined, so the admin tools can reach them there (see admin.js tellRooms). */
export const noteRoom = (env, pid, room) =>
  env.DB.prepare('INSERT INTO rooms_seen (player_id, room, at) VALUES (?1, ?2, ?3) ON CONFLICT (player_id, room) DO UPDATE SET at = ?3')
    .bind(pid, room, Date.now()).run();

/** Forgets the rooms a player joined (when their account goes). */
export const forgetRooms = (env, pid) => env.DB.prepare('DELETE FROM rooms_seen WHERE player_id = ?1').bind(pid);

// ---- replies ----

/** An error the player should see, with its HTTP status. */
export class Problem extends Error {
  constructor(message, status = 400) { super(message); this.status = status; }
}

export async function readJson(request) {
  try { const o = await request.json(); return o && typeof o === 'object' ? o : {}; } catch { return {}; }
}

export const json = (o, status = 200) => new Response(JSON.stringify(o), { status, headers: { 'content-type': 'application/json' } });
