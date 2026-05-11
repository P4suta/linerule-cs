using System.CommandLine;
using Linerule.Cli;
using Linerule.Diagnostics.Storage;
using Linerule.Platform.Windows.Diagnostics;

// Bootstrap diagnostics FIRST — before any subcommand body runs. The
// crash-dump hook must be installed before window/COM/native code can
// possibly throw an SEH or AccessViolation, and the persistent event
// sink must exist before the first log line is emitted (Logger.For
// throws if not initialized — surfacing bootstrap-order bugs immediately,
// by design).
//
// The `using var sink = …` is what guarantees graceful shutdown updates
// `runs.ended_at_utc` and checkpoints the WAL: when control leaves this
// top-level scope (normal exit OR a top-level catch), Dispose runs.
// See ADR-0012 for the writer-only / external-analysis rationale.
using var sink = new SqliteEventSink(SqlitePath.DefaultPath(), RunMetadata.Capture(args));
Logger.Initialize(fileSink: sink);
CrashDump.Install();

return await CliBuilder.Build().Parse(args).InvokeAsync().ConfigureAwait(false);
