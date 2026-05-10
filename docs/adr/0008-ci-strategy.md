# ADR-0008: CI speed and fail-fast

**Status**: Accepted
**Date**: 2026-05-11

## Context

CI feedback latency directly bounds iteration speed. The Rust side keeps the pipeline tight: one matrix-of-one runner, fail-fast on first error.

## Decision

- **Triggers**: `push` to `main`, `pull_request`. **No `on: schedule`** (memory: `feedback_no_cron_in_repos`); Dependabot is the only allowed scheduled job.
- **Concurrency**: same-ref runs cancel each other.
- **All actions SHA-pinned**, Dependabot bumps weekly (memory: `feedback_prefer_latest_not_pinning`).
- **Coverage**: collected and reported as a sticky PR comment via `irongut/CodeCoverageSummary` + `marocchino/sticky-pull-request-comment`; **not gated at 100%** (memory: `feedback_coverage_is_indicator_not_target`).

### Job topology

| Job | Runner | When | Purpose |
|---|---|---|---|
| `build-test-linux` | ubuntu-latest | always | Cross-platform build + test + coverage publish |
| `build-test-windows` | windows-latest | always | WinAppSDK runtime build + test + binary upload |
| `aot-canary` | windows-latest | always (`continue-on-error`) | `<PublishAot>` regression canary |
| `format` | ubuntu-latest | always | CSharpier check + `dotnet format` |
| `strict-code` | ubuntu-latest | always | `Linerule.XTask` banned-regex sweep |
| `spell-check` | ubuntu-latest | always | typos |
| `actionlint` | ubuntu-latest | always | actionlint |
| `vuln-check` | ubuntu-latest | always | `dotnet list package --vulnerable` (fails) + `--deprecated` (info) |
| `dependency-review` | ubuntu-latest | PR only | `actions/dependency-review-action`, fail-on-severity: high |
| `conventional-commits` | ubuntu-latest | PR only | PR title format |
| **`codeql.analyze`** (separate workflow) | windows-latest | always | `security-and-quality` query suite |
| **`dependabot-automerge`** (separate workflow) | ubuntu-latest | dependabot PR | auto-merge patch/minor; majors need human review |

### Why split build into Linux + Windows

- Linux runner is fast and validates that the `net10.0` projects (Core / Config / Platform) genuinely don't drag in Windows-only assets.
- Windows runner exercises the WinAppSDK runtime path (the only path where the overlay actually executes) and is the only runner CodeQL can use because the `net10.0-windows` TFM resolves there.

## Consequences

- A typical PR sees Linux feedback in ~3 minutes; Windows in ~8 minutes; full CI signal under 10 minutes.
- Coverage trends visible inline on each PR via the sticky comment.
- Vulnerable / deprecated package leaks fail fast at the `vuln-check` job.
- Auto-merge for patch/minor Dependabot PRs minimizes maintenance overhead while major bumps stay human-reviewed.
