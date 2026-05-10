# linerule-cs

Reading-ruler overlay for Windows. C# / .NET 10 + Windows App SDK 2.0 + Windows.UI.Composition (via the PowerToys `ICompositorDesktopInterop` pattern). The C# dual of [`linerule`](../linerule/) (Rust / winit / vello / wgpu / peniko).

## Why this exists

`linerule` (Rust) ships v0.1 with a `LWA_COLORKEY` workaround — `wgpu` doesn't expose DirectComposition, so per-pixel alpha is faked by routing pure black to transparent and shifting the dim mask to near-black `(8,8,8)`. The architectural cost is a lie in `Rgba::DefaultMask`.

`Windows.UI.Composition.Compositor` + `ICompositorDesktopInterop.CreateDesktopWindowTarget` sits directly on DirectComposition. With `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOREDIRECTIONBITMAP` (LAYERED owns cross-process click routing, NOREDIRECTIONBITMAP keeps DComp draw direct), true per-pixel alpha is available on Day 1 and `Rgba.DefaultMask` is restored to pure black `(0,0,0,0xCC)`. See [ADR-0009 v3](docs/adr/0009-transparency-via-dcomp.md) for the empirical trail.

This is the architectural payoff that justifies the rewrite. The two implementations coexist as parallel ports; neither is freezing.

## Layout

```
src/
  Linerule.Core/       net10.0           ADTs, render(), reduce() — pure
  Linerule.Config/     net10.0           Tomlyn schema + diagnostics
  Linerule.Platform/   net10.0-windows…  Composition + Win32 click-through + hotkeys
  Linerule.Cli/        net10.0-windows…  System.CommandLine entry
  Linerule.XTask/      net10.0           defensive lint gate
tests/                                   xUnit v3 + FsCheck v3 + Verify
docs/adr/                                ADRs (mirror Rust 1:1)
```

## Build (Docker-first)

The dev container holds .NET 10 SDK + every Linux-side tool — host needs only Docker.

```sh
just docker-build  # one-time: build the dev image
just build         # Release build of the whole solution
just test          # All tests + branch-coverage collection
just coverage      # Branch-coverage report (advisory floor — see ADR-0008)
just lint          # CSharpier + dotnet format + typos + actionlint + xtask strict-code
just shell         # interactive bash inside the container
just ci            # Local replica of the GHA pipeline
```

`Linerule.Core` / `Linerule.Config` / `Linerule.Platform` (TFM `net10.0`) build and test fully in the container.

`Linerule.Platform.Windows` / `Linerule.Cli` (TFM `net10.0-windows10.0.22621.0`) **build** in the container too — WinAppSDK reference assemblies are platform-neutral. The produced executable only **runs** on Windows; for that, copy the `artifacts/publish` output to a Windows host or run from `\\wsl.localhost\Ubuntu\…` after `just publish`. See ADR-0005.

## Tooling (Day 1)

All baked into the dev container; no host-side install required.

- **CSharpier** — opinionated C# formatter, restored as a local dotnet tool from `.config/dotnet-tools.json`.
- **dotnet format** — Roslyn-driven formatter that complements CSharpier (CSharpier owns layout, dotnet-format owns analyzer-driven fixes).
- **typos** — repo-wide spell check. Allowlist in `_typos.toml`.
- **actionlint** — GitHub Actions linter for `.github/workflows/*`.
- **commitlint** — Conventional Commits enforcement. Local hook via lefthook; CI via `wagoid/commitlint-github-action`. Config: `commitlint.config.js`.
- **lefthook** — git hooks runner. `just hooks` installs the hooks; the binary itself runs in the container.
- **Linerule.XTask `strict-code`** — repo-specific banned-regex sweep (memory: `feedback_defensive_gates_upfront`).

```sh
# First-time setup on a fresh clone
just docker-build              # build dev image
just hooks                     # install lefthook git hooks
```

## CI

`.github/workflows/ci.yml` runs the following jobs in parallel on every PR / `main` push:

- **build-test-linux** — `dotnet build/test` for cross-platform projects, with coverage collection and a sticky PR comment.
- **build-test-windows** — full stack including WinAppSDK runtime; uploads single-file binary artifact.
- **aot-canary** — `<PublishAot>` regression canary (`continue-on-error: true`).
- **format** — CSharpier check + `dotnet format --verify-no-changes`.
- **strict-code** — `Linerule.XTask` banned-regex sweep.
- **typos** / **actionlint** / **conventional-commits** (PR only).
- **vuln-check** — `dotnet list package --vulnerable` (fails) + `--deprecated` (info).
- **dependency-review** (PR only) — `actions/dependency-review-action`, `fail-on-severity: high`.

`.github/workflows/codeql.yml` runs CodeQL `security-and-quality` queries on Windows.

`.github/workflows/dependabot-automerge.yml` auto-merges Dependabot patch / minor PRs.

Every action is SHA-pinned (memory: `feedback_prefer_latest_not_pinning`); Dependabot bumps `nuget` / `npm` / `docker` / `github-actions` weekly.

## ADR pointers

- `docs/adr/0001-tech-stack.md` — WinAppSDK 2.0 / CsWin32 / Tomlyn / xUnit v3 rationale
- `docs/adr/0009-transparency-via-dcomp.md` — counter-ADR to Rust 0009 (no color-key)
- `docs/adr/0008-ci-strategy.md` — CI job topology + SHA-pin policy
- `docs/adr/0005-build-environment-exception.md` — Docker-first build; Windows is a runtime exception
