# linerule-cs — task entry points.
#
# Default: every recipe routes through the dev container
# (`docker compose run --rm dev …`) so the host needs only Docker.
# When INSIDE_CONTAINER=1 (set by Dockerfile), recipes run native — no nesting.
#
# ADR-0005 documents that the Windows-host build of Linerule.Platform.Windows /
# Linerule.Cli is the only step that *runtime*-requires Windows; the build
# itself is expected to succeed in this Linux container too.
#
# ----- Build configurations -----
#
# Debug is the default (matches dotnet's own default + standard .NET dev flow):
# faster incremental compile, full PDB, DEBUG symbol, no optimizations,
# LINERULE_LOG=*=Debug (verbose) wired in.
#
# Release is what `just publish` produces: optimized, trimmed PDB,
# LINERULE_LOG=*=Info default (matches the Logger fallback).
#
# Recipes accept an optional `*config` param so you can opt into either
# from any recipe: `just build`, `just build Release`, `just test Debug`,
# `just publish` (always Release — release-only by intent).
#
# ----- Build output -----
#
# Every dotnet invocation passes:
#   --nologo                  — drop the .NET banner
#   -tl:on                    — Terminal Logger (.NET 9+) groups errors per project
#   -clp:Summary;ErrorsOnly  — fallback console logger only emits errors + summary
#
# Override with env: `JUST_DOTNET_V=normal` (verbosity), `JUST_DOTNET_TL=off`.

inside := env_var_or_default("INSIDE_CONTAINER", "0")
docker_run := "docker compose run --rm dev"

dotnet_v := env_var_or_default("JUST_DOTNET_V", "minimal")
dotnet_tl := env_var_or_default("JUST_DOTNET_TL", "on")
# Note: -clp:'Summary;ErrorsOnly' must be single-quoted — `;` is a shell
# command separator and would otherwise break out of the dotnet invocation.
dotnet_flags := "--nologo -v " + dotnet_v + " -tl:" + dotnet_tl + " -clp:'Summary;ErrorsOnly'"

# Verbose log spec for dev recipes (Debug + Trace for high-frequency systems
# off by default — flip subsystems explicitly when chasing a bug).
dev_log := env_var_or_default("LINERULE_LOG", "*=Debug,WndProc=Info,Heartbeat=Info,CursorTracker=Info")

dotnet := if inside == "1" { "dotnet" } else { docker_run + " dotnet" }
typos_bin := if inside == "1" { "typos" } else { docker_run + " typos" }
actionlint_bin := if inside == "1" { "actionlint" } else { docker_run + " actionlint" }
lefthook_bin := if inside == "1" { "lefthook" } else { docker_run + " lefthook" }
sh := if inside == "1" { "bash -lc" } else { docker_run + " bash -lc" }

default:
    @just --list

# ----- one-shot environment -----

# Build the dev image. First-time setup; rerun after Dockerfile edits.
docker-build:
    docker compose build

# Drop into an interactive shell inside the dev container.
shell:
    {{docker_run}} bash

# Tear down volumes + image (used when Dockerfile changes invalidate caches).
clean-docker:
    docker compose down --volumes --rmi local

# ----- .NET workflow (Debug = default) -----

restore:
    {{dotnet}} restore --nologo -v {{dotnet_v}}

# Build (Debug by default — fast incremental, full PDB).
build *config="Debug":
    {{dotnet}} build -c {{config}} {{dotnet_flags}}

build-release: (build "Release")

# Build the Windows-runtime project only — the iteration sweet spot when
# you're touching OverlayWindow / HotkeyHost / CompositionRenderer.
build-platform *config="Debug":
    {{dotnet}} build src/Linerule.Platform.Windows -c {{config}} {{dotnet_flags}}

test *config="Debug":
    {{dotnet}} test -c {{config}} {{dotnet_flags}} \
        --collect:"XPlat Code Coverage" \
        --results-directory artifacts/test-results

test-release: (test "Release")

# Branch coverage report (advisory floor — ADR-0008 + memory feedback_coverage_is_indicator_not_target).
coverage:
    {{dotnet}} test -c Release {{dotnet_flags}} \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:Threshold=80 \
        /p:ThresholdType=branch \
        /p:ThresholdStat=total

# `dotnet watch` style hot loop on the platform project — re-runs tests on save.
# Use during diagnostic-layer or core-logic iteration.
watch:
    {{dotnet}} watch --project tests/Linerule.Core.Tests test {{dotnet_flags}}

# Run the overlay (Windows host only at runtime — Linux-built bin won't actually run).
# Defaults to Debug + verbose LINERULE_LOG so the JSONL stream is informative.
run *args:
    LINERULE_LOG="{{dev_log}}" {{dotnet}} run --project src/Linerule.Cli -c Debug {{dotnet_flags}} -- {{args}}

run-release *args:
    {{dotnet}} run --project src/Linerule.Cli -c Release {{dotnet_flags}} -- {{args}}

# ----- formatters / linters -----

format-check:
    {{dotnet}} format --verify-no-changes --severity info

format:
    {{dotnet}} format --severity info

format-cs:
    {{dotnet}} tool restore
    {{dotnet}} csharpier format .

format-cs-check:
    {{dotnet}} tool restore
    {{dotnet}} csharpier check .

typos:
    {{typos_bin}} --color always

actionlint:
    {{actionlint_bin}} -color

xtask-strict:
    {{dotnet}} run --project src/Linerule.XTask -- strict-code

lint: format-cs-check format-check typos actionlint xtask-strict

# ----- hooks -----

# Install lefthook git hooks. Run from the host; the binary is in the container.
hooks:
    {{lefthook_bin}} install

# ----- release / publish -----

# AOT single-file (Release-only by intent).
publish-aot:
    {{dotnet}} publish src/Linerule.Cli -c Release -r win-x64 {{dotnet_flags}} \
        /p:PublishAot=true /p:StripSymbols=true \
        -o artifacts/aot

# Self-contained Release build for ad-hoc local runs (\\wsl.localhost\... path).
# PublishSingleFile is OFF — WindowsAppSDK 2.x errors out with "PublishSingleFile
# requires EnableMsixTooling" unless we go the full MSIX packaging route, which
# is overkill for a dev binary. The multi-dll layout under artifacts/publish is
# fine; for a single-file dist, see the AOT target above.
publish:
    {{dotnet}} publish src/Linerule.Cli -c Release -r win-x64 {{dotnet_flags}} \
        /p:SelfContained=true \
        -o artifacts/publish

# Self-contained Debug build with full PDB (for `dotnet-dump`, `WinDbg`,
# `dotnet-trace` attach scenarios — keep symbols, keep optimizations off).
# Runs at the same `\\wsl.localhost\...\artifacts\publish-debug\linerule.exe` path
# as the Release publish, just with debuggable bits.
publish-debug:
    {{dotnet}} publish src/Linerule.Cli -c Debug -r win-x64 {{dotnet_flags}} \
        /p:SelfContained=true \
        /p:DebugType=portable /p:DebugSymbols=true \
        -o artifacts/publish-debug

# Local replica of the GHA pipeline.
ci: restore build-release test-release coverage lint

clean:
    rm -rf artifacts/ src/**/bin src/**/obj tests/**/bin tests/**/obj

# ----- diagnostics -----

# Tail the structured JSONL log produced by the running overlay (Windows host).
# From WSL: `just logs-tail` shows the WSL path; `just logs-tail | jq ...` works
# directly since /mnt/c/... is readable from the container.
logs-tail:
    @echo "JSONL log paths:"
    @echo "  Windows: %TEMP%\\linerule.jsonl"
    @echo "  WSL    : /mnt/c/Users/$USER/AppData/Local/Temp/linerule.jsonl"
    @if [ -f /mnt/c/Users/$USER/AppData/Local/Temp/linerule.jsonl ]; then \
        tail -f /mnt/c/Users/$USER/AppData/Local/Temp/linerule.jsonl; \
    else \
        echo "(no log yet — run the overlay once to create it)"; \
    fi

# Pretty-print the JSONL log, optionally filtered by subsystem.
# Example: `just logs-pretty WndProc` shows only WndProc entries.
logs-pretty subsystem="":
    @if [ -z "{{subsystem}}" ]; then \
        cat /mnt/c/Users/$USER/AppData/Local/Temp/linerule.jsonl 2>/dev/null | jq -C '.'; \
    else \
        cat /mnt/c/Users/$USER/AppData/Local/Temp/linerule.jsonl 2>/dev/null | jq -C 'select(.subsystem == "{{subsystem}}")'; \
    fi

# List recent crash dumps (newest first).
crash-list:
    @ls -lt /mnt/c/Users/$USER/AppData/Local/Temp/linerule-crash-*.json 2>/dev/null | head -10 || echo "no crash dumps in %TEMP%"

# Pretty-print the most recent crash dump.
crash-latest:
    @ls -t /mnt/c/Users/$USER/AppData/Local/Temp/linerule-crash-*.json 2>/dev/null | head -1 | xargs -r cat | jq -C '.'

# Wipe the JSONL log (start fresh — useful before reproducing a bug).
logs-clear:
    @rm -f /mnt/c/Users/$USER/AppData/Local/Temp/linerule.jsonl
    @echo "cleared %TEMP%\\linerule.jsonl"
