-- BookBuddies Unity server tables. The Worker makes these by itself on first use,
-- so running this file is optional (npm run db:schema).

CREATE TABLE IF NOT EXISTS players (id TEXT PRIMARY KEY, name TEXT NOT NULL, pet TEXT, recovery_code TEXT UNIQUE,
  coins INTEGER NOT NULL DEFAULT 0, is_admin INTEGER NOT NULL DEFAULT 0, mute_until INTEGER NOT NULL DEFAULT 0,
  ban_until INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL, last_seen INTEGER);

CREATE TABLE IF NOT EXISTS tokens (hash TEXT PRIMARY KEY, player_id TEXT NOT NULL, created_at INTEGER NOT NULL);

CREATE INDEX IF NOT EXISTS tokens_player ON tokens (player_id);

CREATE TABLE IF NOT EXISTS limits (kind TEXT NOT NULL, ip TEXT NOT NULL, day TEXT NOT NULL, n INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (kind, ip, day));

CREATE TABLE IF NOT EXISTS blocked_words (word TEXT PRIMARY KEY);

CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT NOT NULL);

-- Every change made with the admin tools (who, what, to whom), newest last. The Worker keeps the latest 5000.
CREATE TABLE IF NOT EXISTS admin_log (id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL, admin_id TEXT NOT NULL,
  admin_name TEXT NOT NULL, action TEXT NOT NULL, target_id TEXT, target_name TEXT, detail TEXT);

CREATE INDEX IF NOT EXISTS admin_log_target ON admin_log (target_id);

-- Each player's pets (up to six). The active one's look is also players.pet. See src/pets.js.
CREATE TABLE IF NOT EXISTS pets (player_id TEXT NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, look TEXT NOT NULL,
  active INTEGER NOT NULL DEFAULT 0, born INTEGER NOT NULL, PRIMARY KEY (player_id, id));

-- The wallet (src/wallet.js): coins and Book Fair tickets are ledgers (a balance is SUM(amount); each (kind, ref) pays once),
-- and econ_state keeps the Book Fair's counters. The website's tables, as they are.
CREATE TABLE IF NOT EXISTS coin_tx (id INTEGER PRIMARY KEY AUTOINCREMENT, player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  kind TEXT NOT NULL, ref TEXT NOT NULL, amount INTEGER NOT NULL, day TEXT NOT NULL, created_at INTEGER NOT NULL);

CREATE UNIQUE INDEX IF NOT EXISTS coin_tx_once_idx ON coin_tx (player_id, kind, ref);

CREATE INDEX IF NOT EXISTS coin_tx_day_idx ON coin_tx (player_id, day);

CREATE TABLE IF NOT EXISTS fair_tx (id INTEGER PRIMARY KEY AUTOINCREMENT, player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  kind TEXT NOT NULL, ref TEXT NOT NULL, amount INTEGER NOT NULL, day TEXT NOT NULL, created_at INTEGER NOT NULL);

CREATE UNIQUE INDEX IF NOT EXISTS fair_tx_once_idx ON fair_tx (player_id, kind, ref);

CREATE TABLE IF NOT EXISTS econ_state (player_id TEXT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE, pity INTEGER NOT NULL,
  balls INTEGER NOT NULL, tix INTEGER NOT NULL, drops INTEGER NOT NULL, fish_at INTEGER NOT NULL, ups TEXT NOT NULL, freecap TEXT);

-- Tales of Pages (src/quest.js): co-op tales saved for the party, the Bramble Road raid and everyone's hits on it,
-- notes to players (tale news, raid won), and the daily tale's board (one row per player per day).
CREATE TABLE IF NOT EXISTS quest_runs (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, title TEXT, chapter INTEGER NOT NULL DEFAULT 1, party TEXT,
  state TEXT, visibility TEXT NOT NULL DEFAULT 'link', over INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL, online INTEGER NOT NULL DEFAULT 0, pets TEXT, srev INTEGER NOT NULL DEFAULT 0, last_by TEXT, kind TEXT);

CREATE INDEX IF NOT EXISTS quest_runs_owner_idx ON quest_runs (owner_id, updated_at);

CREATE TABLE IF NOT EXISTS quest_raids (id TEXT PRIMARY KEY, club TEXT NOT NULL, n INTEGER NOT NULL, boss INTEGER NOT NULL, lvl INTEGER NOT NULL,
  hp INTEGER NOT NULL, max INTEGER NOT NULL, started_at INTEGER NOT NULL, ends_at INTEGER NOT NULL, done_at INTEGER);

CREATE INDEX IF NOT EXISTS quest_raids_club_idx ON quest_raids (club, started_at);

CREATE TABLE IF NOT EXISTS quest_raid_hits (raid_id TEXT NOT NULL, player_id TEXT NOT NULL, dmg INTEGER NOT NULL DEFAULT 0,
  tries INTEGER NOT NULL DEFAULT 0, day TEXT, pet TEXT, last_at INTEGER, PRIMARY KEY (raid_id, player_id));

CREATE TABLE IF NOT EXISTS notes (id INTEGER PRIMARY KEY AUTOINCREMENT, to_id TEXT NOT NULL, from_id TEXT NOT NULL, kind TEXT NOT NULL,
  game TEXT, day TEXT NOT NULL, created_at INTEGER NOT NULL, read INTEGER NOT NULL DEFAULT 0);

CREATE INDEX IF NOT EXISTS notes_to_idx ON notes (to_id, read);

CREATE UNIQUE INDEX IF NOT EXISTS notes_once_idx ON notes (from_id, to_id, kind, day, game);

CREATE TABLE IF NOT EXISTS quest_daily (day TEXT NOT NULL, player_id TEXT NOT NULL, score INTEGER NOT NULL DEFAULT 0, label TEXT, pet TEXT,
  look TEXT, state TEXT, over INTEGER NOT NULL DEFAULT 0, at INTEGER NOT NULL, PRIMARY KEY (day, player_id));

CREATE INDEX IF NOT EXISTS quest_daily_board_idx ON quest_daily (day, score);
