-- linerule events store schema (writer-only side).
--
-- Authoritative source for the PR 2 design. Tables are STRICT (SQLite 3.37+)
-- so column-affinity sloppiness becomes a hard error at insert time.
-- All timestamps stored two ways:
--   * ts_utc      — ISO-8601 string for human/JOIN-by-day queries.
--   * ts_unix_ns  — nanosecond Unix epoch INTEGER for index range scans.
--
-- Indices target the three dominant query shapes:
--   * "events for run X in order"               -> idx_events_run
--   * "subsystem timeline"                      -> idx_events_sub_ts
--   * "errors+warnings (level >= 3) by time"    -> idx_events_lvl_ts (partial)
--   * "trace activity across subsystems"        -> idx_events_activity (partial)
--   * "runs ordered by start"                   -> idx_runs_started

PRAGMA journal_mode = WAL;
PRAGMA synchronous  = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA temp_store   = MEMORY;
PRAGMA busy_timeout = 5000;

CREATE TABLE IF NOT EXISTS runs (
  run_id         BLOB NOT NULL
                 CHECK (length(run_id) = 16)
                 PRIMARY KEY,
  started_at_utc TEXT NOT NULL,
  ended_at_utc   TEXT NULL,
  version        TEXT NOT NULL,
  build_config   TEXT NOT NULL,
  args           TEXT NOT NULL,
  hostname       TEXT NOT NULL,
  pid            INTEGER NOT NULL,
  os_version     TEXT NULL,
  dropped_count  INTEGER NOT NULL DEFAULT 0
) STRICT;

CREATE TABLE IF NOT EXISTS events (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id       BLOB NOT NULL
               CHECK (length(run_id) = 16)
               REFERENCES runs(run_id) ON DELETE CASCADE,
  ts_utc       TEXT NOT NULL,
  ts_unix_ns   INTEGER NOT NULL,
  level        INTEGER NOT NULL,
  subsystem    TEXT NOT NULL,
  step         TEXT NOT NULL,
  session_id   BLOB NULL
               CHECK (session_id IS NULL OR length(session_id) = 16),
  frame_seq    INTEGER NULL,
  activity_id  TEXT NULL,
  fields_json  TEXT NULL,
  exception_json TEXT NULL
) STRICT;

CREATE INDEX IF NOT EXISTS idx_events_run      ON events(run_id, id);
CREATE INDEX IF NOT EXISTS idx_events_sub_ts   ON events(subsystem, ts_unix_ns);
CREATE INDEX IF NOT EXISTS idx_events_lvl_ts   ON events(level, ts_unix_ns) WHERE level >= 3;
CREATE INDEX IF NOT EXISTS idx_events_activity ON events(activity_id) WHERE activity_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_runs_started    ON runs(started_at_utc);
