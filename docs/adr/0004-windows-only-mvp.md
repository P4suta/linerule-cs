# ADR-0004: Windows-only for v0.1

**Status**: Accepted
**Date**: 2026-05-11

## Context

A reading-ruler overlay is most useful on the OS the user actually reads on. v0.1 ships Windows only; macOS and Linux are deferred.

## Decision

Ship Windows v0.1 first. macOS comes later (likely via `CALayer` per `Layer`, the Cocoa equivalent of `Microsoft.UI.Composition`). Linux requires Wayland-or-X11 specifics; not in scope before macOS.

## Consequences

- `Linerule.Platform.Windows` is the only platform impl shipped.
- The `IOverlaySurface` / `IHotkeyHost` / `IMouseTracker` interfaces are the only cross-platform surface; no premature porting code.
- The mock impls let core tests run on Linux Docker.
