# linerule-cs — task entry points. Routes through Docker unless INSIDE_CONTAINER=1.

inside := env_var_or_default("INSIDE_CONTAINER", "0")

dev_running := `docker compose ps --status running --services 2>/dev/null | grep -c '^dev$' 2>/dev/null || true`
docker_run := if dev_running == "0" { "docker compose run --rm dev" } else { "docker compose exec dev" }

dotnet_v := env_var_or_default("JUST_DOTNET_V", "minimal")
dotnet_tl := env_var_or_default("JUST_DOTNET_TL", "on")
dotnet_flags := "--nologo -v " + dotnet_v + " -tl:" + dotnet_tl + " -clp:'Summary;ErrorsOnly'"

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

# Start a long-lived dev container. Once up, every `just <recipe>` routes
# through `docker compose exec` instead of `docker compose run --rm`,
# saving ≈1.5 s per invocation. Use this when you're iterating on builds.
dev-up:
    docker compose up -d dev
    @echo "dev container is up — `just <recipe>` now uses docker exec (faster)."
    @echo "stop with `just dev-down`."

# Stop the long-lived dev container — recipes go back to `run --rm`.
dev-down:
    docker compose stop dev
    @echo "dev container stopped — `just <recipe>` will spin up a fresh container per call."

# ----- .NET workflow (Debug = default) -----

restore:
    {{dotnet}} restore --nologo -v {{dotnet_v}}

build *config="Debug":
    {{dotnet}} build -c {{config}} {{dotnet_flags}}

build-release: (build "Release")

# Inner-loop alias: skips restore + analyzers (Debug already disables them).
b *config="Debug":
    {{dotnet}} build -c {{config}} --no-restore {{dotnet_flags}}

# Build the Windows-runtime project only — the iteration sweet spot when
# you're touching OverlayWindow / HotkeyHost / CompositionRenderer.
build-platform *config="Debug":
    {{dotnet}} build src/Linerule.Platform.Windows -c {{config}} {{dotnet_flags}}

# Inner-loop platform build (no restore, faster).
bp *config="Debug":
    {{dotnet}} build src/Linerule.Platform.Windows -c {{config}} --no-restore {{dotnet_flags}}

test *config="Debug":
    {{dotnet}} test -c {{config}} {{dotnet_flags}} \
        --collect:"XPlat Code Coverage" \
        --results-directory artifacts/test-results

# Inner-loop test alias (no restore).
t *config="Debug":
    {{dotnet}} test -c {{config}} --no-restore {{dotnet_flags}} \
        --collect:"XPlat Code Coverage" \
        --results-directory artifacts/test-results

test-release: (test "Release")

# Branch coverage report (advisory; see ADR-0008).
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

# Apply typo fixes in-place across the whole tree. Lefthook already runs
# `typos --write-changes` over staged files at pre-commit, so the typical
# loop never needs this recipe; reach for it after a large refactor when
# many files have changed and you want to fix typos before commit.
# `_typos.toml` allowlist still applies — Marshalling / HWND / DComp etc.
# stay protected.
typos-fix:
    {{typos_bin}} --write-changes --color always

actionlint:
    {{actionlint_bin}} -color

xtask-strict:
    {{dotnet}} run --project src/Linerule.XTask -- strict-code

lint: format-cs-check format-check typos actionlint xtask-strict

# ----- hooks -----

# One-time setup after clone. Installs commitlint's node deps inside the
# container (npm is unavailable on the WSL host by design — Docker-only),
# then writes the .git/hooks/<event> scripts. After this, every commit and
# push runs the lefthook pipeline below.
hooks:
    {{docker_run}} npm install --no-audit --no-fund
    {{lefthook_bin}} install

# Lefthook command targets. lefthook.yml shells out to these so the hook
# steps reuse the same docker_run / dotnet / typos_bin variables as the
# rest of this Justfile — i.e., hooks behave identically whether `dev` is
# warm (exec) or cold (run --rm), and never assume a tool exists on the
# WSL host. Underscore prefix keeps them out of `just --list`.

_hook-csharpier-format *files:
    {{dotnet}} tool restore
    {{dotnet}} csharpier format {{files}}

_hook-dotnet-format-style *files:
    {{dotnet}} format style --include {{files}} --no-restore

_hook-dotnet-format-analyzers *files:
    {{dotnet}} format analyzers --include {{files}} --no-restore

_hook-typos-fix *files:
    {{typos_bin}} --write-changes {{files}}

_hook-actionlint *files:
    {{actionlint_bin}} -color {{files}}

_hook-xtask-strict:
    {{dotnet}} run --project src/Linerule.XTask -- strict-code

_hook-commitlint msgfile:
    {{docker_run}} npx --no -- commitlint --edit {{msgfile}}

# ----- release / publish -----

# AOT single-file (Release-only by intent). ADR-0010 Phase 2 made this a
# required CI gate (no longer continue-on-error). win-x64 only — Cli's TFM
# is net10.0-windows so cross-OS publish from Linux is unsupported; CI runs
# the actual publish on windows-latest.
publish-aot:
    {{dotnet}} publish src/Linerule.Cli -c Release -r win-x64 {{dotnet_flags}} \
        /p:PublishAot=true /p:StripSymbols=true \
        -o artifacts/aot

# AOT single-file for the WinExe GUI launcher (Linerule.App). Same shape
# as publish-aot above; runs under the same windows-latest CI gate.
publish-dist-aot:
    {{dotnet}} publish src/Linerule.App -c Release -r win-x64 {{dotnet_flags}} \
        /p:PublishAot=true /p:StripSymbols=true \
        -o artifacts/aot-dist

# AOT publish of the xtask lint CLI. Unlike the Cli (Tomlyn) and App (WinAppSDK),
# XTask has no AOT blockers — it's the one binary in the repo that proves the
# AOT-readiness machinery actually works end-to-end. linux-x64 because XTask
# targets net10.0 (cross-platform) and the dev container is Linux.
publish-xtask-aot:
    {{dotnet}} publish src/Linerule.XTask -c Release -r linux-x64 {{dotnet_flags}} \
        /p:PublishAot=true /p:StripSymbols=true \
        -o artifacts/aot-xtask

# AOT publish of Linerule.Diagnostics.Storage as a shared library.
# Library AOT publish IS the verification: NativeAOT runs the full trim/AOT
# analyzer suite over the transitive public surface, so any new reflection
# that creeps into Storage (or its deps consumed via Storage's public API)
# breaks this build. The .so output is not shipped — only the build matters.
publish-storage-aot:
    {{dotnet}} publish src/Linerule.Diagnostics.Storage -c Release -r linux-x64 {{dotnet_flags}} \
        /p:PublishAot=true /p:StripSymbols=true \
        -o artifacts/aot-storage

# Self-contained Release build for ad-hoc local runs (\\wsl.localhost\... path).
# PublishSingleFile is OFF — WindowsAppSDK 2.x errors out with "PublishSingleFile
# requires EnableMsixTooling" unless we go the full MSIX packaging route, which
# is overkill for a dev binary. The multi-dll layout under artifacts/publish is
# fine; for a single-file dist, see the AOT target above.
publish:
    {{dotnet}} publish src/Linerule.Cli -c Release -r win-x64 {{dotnet_flags}} \
        /p:SelfContained=true \
        -o artifacts/publish

# Distribution-shaped Release build — the WinExe GUI launcher (Linerule.App).
# Produces artifacts/publish-dist/Linerule.exe (capital L, distinct from the
# CLI's linerule.exe). PE subsystem = Windows GUI, so Explorer double-click
# goes straight to the overlay with no flashing console window. Same multi-dll
# layout as `publish` — ship the whole artifacts/publish-dist/ directory.
# Diagnostics still land in %APPDATA%\linerule\events.sqlite (ADR-0012).
publish-dist:
    {{dotnet}} publish src/Linerule.App -c Release -r win-x64 {{dotnet_flags}} \
        /p:SelfContained=true \
        -o artifacts/publish-dist

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
