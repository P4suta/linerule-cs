# ADR-0005: Build-environment Docker-only exception

**Status**: Accepted
**Date**: 2026-05-11

## Context

`feedback_docker_only_execution` is a global rule: every `cargo` / `pytest` / `npm` invocation goes through `docker compose run` to avoid host-toolchain drift. linerule (Rust) honors it.

WinAppSDK + Windows App Runtime + Microsoft.UI.Composition + DirectComposition need a Windows host to build and run. Linux containers cannot host the WinAppSDK runtime, the windowing surface, or the Composition target.

## Decision

**Build is fully Dockerized.** A `mcr.microsoft.com/dotnet/sdk:10.0` dev image (`Dockerfile` + `compose.yml`) holds the SDK plus every host-side tool (CSharpier, typos, actionlint, lefthook, just, Node + commitlint). Every `just` recipe routes through `docker compose run --rm dev` unless `INSIDE_CONTAINER=1` is already set.

- `Linerule.Core` / `Linerule.Config` / `Linerule.Platform` target `net10.0` — both build and test fully in the container.
- `Linerule.Platform.Windows` / `Linerule.Cli` target `net10.0-windows10.0.22621.0`. **Build also succeeds in the container** — WinAppSDK reference assemblies are platform-neutral and CsWin32's source generator runs on any .NET host. The produced binary only **runs** on Windows; copy `artifacts/publish` output to the host or run via `\\wsl.localhost\…`.

The Docker-only rule (memory: `feedback_docker_only_execution`) is therefore preserved. The "Windows exception" is narrowed to **runtime**, not build: a Windows OS is required only when actually launching the overlay or running an integration test that opens a real HWND.

## Consequences

- Contributors need only Docker on the host. No `winget install Microsoft.DotNet.SDK.10` step.
- CI runs `windows-latest` to additionally exercise the runtime path on a real HWND.
- Native AOT publish (`just publish-aot`) is built in the container; running and verifying the AOT binary still requires Windows.
