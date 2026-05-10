# linerule-cs — task entry points.
#
# Default: every recipe routes through the dev container
# (`docker compose run --rm dev …`) so the host needs only Docker.
# When INSIDE_CONTAINER=1 (set by Dockerfile), recipes run native — no nesting.
#
# ADR-0005 documents that the Windows-host build of Linerule.Platform.Windows /
# Linerule.Cli is the only step that *runtime*-requires Windows; the build
# itself is expected to succeed in this Linux container too.

inside := env_var_or_default("INSIDE_CONTAINER", "0")
docker_run := "docker compose run --rm dev"

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

# ----- .NET workflow -----

restore:
    {{dotnet}} restore

build *args:
    {{dotnet}} build -c Release --nologo {{args}}

test *args:
    {{dotnet}} test -c Release --nologo \
        --collect:"XPlat Code Coverage" \
        --results-directory artifacts/test-results {{args}}

# Branch coverage report (advisory floor — see ADR-0008 + memory feedback_coverage_is_indicator_not_target).
coverage:
    {{dotnet}} test -c Release --nologo \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:Threshold=80 \
        /p:ThresholdType=branch \
        /p:ThresholdStat=total

# Run the overlay (Windows host only at runtime — Linux-built bin won't actually run).
run *args:
    {{dotnet}} run --project src/Linerule.Cli -- {{args}}

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

# ----- release / canary -----

publish-aot:
    {{dotnet}} publish src/Linerule.Cli -c Release -r win-x64 \
        /p:PublishAot=true /p:StripSymbols=true \
        -o artifacts/aot

publish:
    {{dotnet}} publish src/Linerule.Cli -c Release -r win-x64 \
        /p:PublishSingleFile=true /p:SelfContained=true \
        /p:PublishTrimmed=true \
        -o artifacts/publish

# Local replica of the GHA pipeline.
ci: restore build test coverage lint

clean:
    rm -rf artifacts/
