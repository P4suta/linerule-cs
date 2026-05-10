# ADR-0007: Release pipeline

**Status**: Accepted
**Date**: 2026-05-11

## Context

The Rust side uses `cargo-dist`. The C# side needs an analogue: tag → build → bundle → upload to GitHub Releases.

## Decision

v0.1 ships as a self-contained `dotnet publish` for `win-x64` (and `win-arm64` opportunistically). The artifact is a single-file executable. Native AOT is aspirational (ADR-0010).

The release workflow is triggered on tag `v*.*.*`. Steps:

1. `dotnet publish src/Linerule.Cli -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true`.
2. (When AOT is green) `dotnet publish ... /p:PublishAot=true`.
3. Upload the produced binary to the matching GitHub Release.

GitHub Actions are SHA-pinned (memory: `feedback_prefer_latest_not_pinning`); Dependabot bumps weekly.

## Consequences

- No MSIX in v0.1 (deferred).
- Auto-update is deferred — the v0.1 binary is downloaded manually from Releases.
