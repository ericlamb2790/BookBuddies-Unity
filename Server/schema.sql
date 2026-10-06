-- BookBuddies Unity server tables. The Worker makes these by itself on first use,
-- so running this file is optional (npm run db:schema).

CREATE TABLE IF NOT EXISTS players (id TEXT PRIMARY KEY, name TEXT NOT NULL, pet TEXT, recovery_code TEXT UNIQUE,
  coins INTEGER NOT NULL DEFAULT 0, is_admin INTEGER NOT NULL DEFAULT 0, mute_until INTEGER NOT NULL DEFAULT 0,
  ban_until INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL, last_seen INTEGER);

CREATE TABLE IF NOT EXISTS tokens (hash TEXT PRIMARY KEY, player_id TEXT NOT NULL, created_at INTEGER NOT NULL);

CREATE INDEX IF NOT EXISTS tokens_player ON tokens (player_id);

CREATE TABLE IF NOT EXISTS finds (player_id TEXT NOT NULL, day TEXT NOT NULL, n INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (player_id, day));

CREATE TABLE IF NOT EXISTS limits (kind TEXT NOT NULL, ip TEXT NOT NULL, day TEXT NOT NULL, n INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (kind, ip, day));

CREATE TABLE IF NOT EXISTS blocked_words (word TEXT PRIMARY KEY);

CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT NOT NULL);
