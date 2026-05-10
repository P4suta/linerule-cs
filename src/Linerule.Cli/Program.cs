using System.CommandLine;
using Linerule.Cli;
using Linerule.Platform.Windows.Diagnostics;

// Bootstrap diagnostics FIRST — before any subcommand body runs. The
// crash-dump hook must be installed before window/COM/native code can
// possibly throw an SEH or AccessViolation, and the JSONL sink must
// exist before the first log line is emitted (Logger.For throws if not
// initialized — surfacing bootstrap-order bugs immediately, by design).
Logger.Initialize();
CrashDump.Install();

return await CliBuilder.Build().Parse(args).InvokeAsync().ConfigureAwait(false);
