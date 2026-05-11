# ADR-0012: SQLite event store — writer-only, analysis is external

**Status**: Accepted
**Date**: 2026-05-11

## Context

The first cut of the diagnostic pipeline persisted events as JSONL at
`%TEMP%\linerule.jsonl` — one JSON object per line, truncate-on-launch.
JSONL got us off the ground (any text tool can read it, `jq` is everywhere)
but three pains accumulated quickly:

- **No cross-run history.** Truncate-on-launch is great for "what did THIS
  run do" and bad for "did this start happening after build N?". Append
  mode shifted the burden to `jq 'select(.ctx.run_id == "…")'` for every
  query, and the file grew unbounded.
- **Schema drift.** Every new `LogField` type silently appears in the JSON
  shape. Downstream tools have to be liberal in what they accept; mistyped
  field names go unnoticed.
- **Query ergonomics.** Even with `jq`, "p99 of `tick_p99_ms` grouped by
  build_config across the last 7 runs" is an ad-hoc reduction; SQL with a
  proper schema is the right shape.

The instinct is to pull DuckDB into the overlay process — embedded
analytics, columnar storage, native Parquet, the works. That conflates two
responsibilities. The overlay binary's job is to ship events without
stalling the UI thread; analysis happens on a developer laptop, in a
notebook, with whichever tool the analyst prefers. Bundling an analysis
engine into the binary widens the dependency surface, makes AOT harder
(DuckDB.NET is reflection-heavy), and inverts the actual usage pattern.

## Decision

The writer's responsibility ends at the artifact. `events.sqlite` is the
contract; any tool that speaks SQLite reads it. The overlay binary ships
exactly one analytic dependency: `Microsoft.Data.Sqlite` 10.x — the same
provider EF Core uses, kept up to date with the .NET 10 SDK.

**Storage layout.** Single persistent DB at
`%APPDATA%\linerule\events.sqlite`. WAL journal mode (`PRAGMA
journal_mode=WAL`) so readers can attach while the overlay is writing —
the user can run `duckdb` queries against a live file. `synchronous=NORMAL`
trades the theoretical "lose the last ~few ms of events on power-cut" for
real throughput; the overlay is not a financial ledger, and the WAL is
checkpointed on graceful shutdown. `busy_timeout=5000` tolerates the
multi-process case (two overlay instances on the same machine; an analyst
running `duckdb` against the live DB; an editor's SQLite preview holding a
shared lock).

**Run metadata.** Every `Logger.Initialize` call creates one row in
`runs(run_id, started_at_utc, ended_at_utc, build_config, exe_version,
argv_json, machine_name)` via `RunMetadata.Capture(argv)`. `events.run_id`
is a foreign key into `runs`; `ended_at_utc` is updated on
`SqliteEventSink.Dispose`. The `using var sink = …` in
`Linerule.Cli.Program` is what guarantees that dispose runs (and therefore
that `ended_at_utc` and WAL checkpoint happen on graceful exit). Crashes
leave `ended_at_utc` NULL — that itself is observable; a `WHERE
ended_at_utc IS NULL` query finds runs that died mid-flight.

**Why not DuckDB embedded.** DuckDB.NET is a wrapper around the C++ engine
plus a managed analytics surface. It pulls a multi-megabyte native
dependency, has its own threading model, and is reflection-heavy enough to
keep `<PublishAot>` red indefinitely. The overlay binary should not ship
an analytics engine. DuckDB on the *analyst's* machine reads SQLite files
natively (`INSTALL sqlite; LOAD sqlite;`), so we get DuckDB's query
ergonomics without coupling the overlay to its runtime.

**Why not Parquet rotation.** Parquet is the right shape for "fixed
schema, append-only, columnar, multi-GB". Our event volume is far below
that — a long debug session is a few MB. Parquet rotation introduces a
file lifecycle (when to roll, how many to keep, how to query across
rolled files) that buys nothing for a single-developer overlay. If the
event volume ever crosses the threshold where SQLite is the bottleneck,
that is a future PR; today's pain is "can't query across runs", not "can't
hold a year of telemetry".

**Why not a custom binary format.** Discoverability. The file is just
SQLite — any IDE, any DBeaver / TablePlus / DataGrip, the `sqlite3` CLI
shipped with macOS, Linux, and the Windows SDK, and Python's stdlib all
open it without setup. The "open with whatever's installed" property is
worth more than the bytes a custom format would save.

The `RingBufferSink` capturing the last N entries in memory is untouched
by this change. It exists for crash-dump replay: when an unhandled
exception fires, `CrashDump` snapshots the ring and writes a separate
JSON dump to `%TEMP%\linerule-crash-*.json`. That is a different
artifact with a different audience (post-mortem, single dump, human-read)
and does not need to share a backend with the long-running event store.

## Consequences

- The overlay binary's analytic dependency stays at one package
  (`Microsoft.Data.Sqlite`), pinned via `Directory.Packages.props` and
  bumped by Dependabot.
- An analyst opens the file with whatever they prefer: `duckdb -c "ATTACH
  '…/events.sqlite' (TYPE sqlite); SELECT …"`, `sqlite3 events.sqlite
  "…"`, Python's `sqlite3` stdlib, DBeaver / TablePlus, or their IDE's
  SQLite preview. The README documents the canonical recipe; the binary
  itself ships no analysis CLI.
- A graceful shutdown is required for `ended_at_utc` to be set and the
  WAL to checkpoint. The `using var sink = …` pattern in
  `Linerule.Cli.Program` enforces this; the `AppDomain.ProcessExit` hook
  in `Logger` is a backstop, not the primary mechanism.
- Crashes are visible as runs with `ended_at_utc IS NULL`. That is a
  feature, not a bug — the user can find them with a one-line query.
- Schema lives as `const string` DDL inside `SqliteSchema.Apply`. CA2100
  ("review SQL for vulnerabilities") demands a literal at the
  `cmd.CommandText = …` callsite to prove no user input flows in, so
  embedded-resource splitting is out — only literal-at-callsite passes
  the analyzer without a `SuppressMessage`. The sibling `schema.sql`
  file is kept as documentation (sqlfluff / sqlite3 CLI / IDE SQL
  plugins preview the canonical shape) and a test
  (`SchemaTests.Sql_file_matches_inline_statements`) pins drift between
  the two so the .sql file stays a faithful mirror. Future migrations
  add another const block + another `cmd.CommandText = …` line; numbered
  `migrations/0002_*.sql` are mirrored the same way.
- 16-byte invariants on `runs.run_id` / `events.run_id` /
  `events.session_id` are encoded as STRICT-compatible
  `BLOB … CHECK (length(...) = 16)` rather than the column-modifier
  `BLOB(16)` (which SQLite STRICT mode rejects — STRICT permits only
  `INT/INTEGER/REAL/TEXT/BLOB/ANY`). The CHECK fires at INSERT time, so
  the invariant remains a hard error rather than column-affinity
  sloppiness.
- The crash-dump path is unchanged: `RingBufferSink` + `CrashDump.Dump`
  still produce `linerule-crash-*.json` snapshots independent of the
  event DB, so a SQLite open failure does not erase the post-mortem.
