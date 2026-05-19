# ADR-0017: Rolling AOT GUI release pipeline

**Status**: Accepted (supersedes the unimplemented release wiring of ADR-0007)
**Date**: 2026-05-20

## Context

CI uploads AOT artifacts (`aot-app-win-x64` and friends) on every push and PR,
but downloading any of them requires opening the GitHub Actions run page,
finding the right job, and navigating into its artifact list. That friction
slows dogfooding — the moment the overlay actually wants to be touched is
right after a merge, not 90 seconds of click-through later.

ADR-0007 specifies the long-run answer (semver `v*.*.*` tag → CLI + GUI
self-contained release), but that wiring was never built; the project does
not yet have a stable v0.x to anchor a semver tag against, and the CLI
binary's distribution story is still in flux.

In the meantime, two things are already true:

- The AOT GUI build (`Linerule.exe`, ~8 MB, win-x64) has been verified
  end-to-end on real hardware (Phase E-A).
- Conventional Commits are enforced repo-wide via commitlint + Dependabot
  `commit-message.prefix`, which is exactly the substrate git-cliff needs
  to auto-generate release notes.

So a lighter mechanism — a "rolling latest" release that always reflects
`main` HEAD — buys most of the user-experience win without locking in the
semver-tag ceremony.

## Decision

Ship a new GitHub Actions workflow (`.github/workflows/release.yml`,
SHA-pinned like the rest of CI) that fires on every push to `main` and on
manual `workflow_dispatch`:

1. **Build** `src/Linerule.App` on `windows-latest` with
   `/p:PublishAot=true /p:StripSymbols=true
   /p:Version=0.0.0-rolling.<yyyymmdd>.<short-sha>`. The version string is
   constructed in-workflow from `git rev-parse --short=7 HEAD` + UTC date;
   `Directory.Build.props` carries no `<Version>`.
2. **Pack** the `artifacts/aot-dist/` output into
   `Linerule-aot-win-x64.zip` and write a sibling `sha256sums.txt`.
3. **Generate** release notes with `orhun/git-cliff-action`, configured by
   `cliff.toml` at repo root. The `tag_pattern` is locked to semver
   (`^v[0-9]+\.[0-9]+\.[0-9]+$`), so the per-push rolling tags are never
   treated as prior release boundaries.
4. **Publish** a fresh GitHub Release on every push. The tag is dynamic
   (`release-0.0.0-rolling.<yyyymmdd>.<short-sha>`); old releases stay in
   the history as immutable artifacts of `main`'s past. We create the
   release as a `draft: true` via `softprops/action-gh-release` (so all
   assets can be staged before the release goes live — see
   "Why dynamic tags" below), then `gh release edit … --draft=false
   --latest --prerelease` flips it to published, retargets the
   `/releases/latest/...` redirect at the new tag, and keeps the
   pre-release badge.

Concurrency group `release` with `cancel-in-progress: true` collapses
back-to-back main pushes onto the latest commit; the previous in-flight
job is cancelled rather than racing.

The download URL becomes
`https://github.com/P4suta/linerule-cs/releases/latest/download/Linerule-aot-win-x64.zip`
— exactly the kind of stable link the existing CI artifact flow could not
provide. `latest` here refers to GitHub's "latest release" pointer (driven
by `--latest`), not a literal tag name.

### Why dynamic tags

GitHub's **Immutable Releases** repo setting (default-on for new repos
since 2025) (a) forbids asset uploads to an already-published release —
which breaks `softprops`'s serial multi-file upload pattern — and (b)
reserves any tag a published release has used, even after the release is
deleted. A fixed `latest` tag, force-recreated on every push, cannot
survive (b); the first run reserves the tag, the second run gets
`tag_name was used by an immutable release` from `gh release edit`.

The `draft: true` → `gh release edit` flow handles (a). The dynamic
`release-<version>` tag handles (b) — every push picks a never-used name
because the version string already encodes date + short SHA. The
`--latest` flag on `gh release edit` is what drives the
`/releases/latest/...` redirect, so users still get a fixed-URL download.

## Non-goals

- **Semver tag-triggered stable releases.** ADR-0007's `v*.*.*` path
  remains the intended approach for the eventual v0.1 stable cut. The two
  flows do not collide: rolling releases use `release-*` tags in their
  own namespace, `v*.*.*` would live separately and is already excluded
  from `cliff.toml`'s `tag_pattern`. ADR-0007 stays valid for that future
  milestone.
- **CLI distribution.** `linerule.exe` (CUI, `src/Linerule.Cli`) is *not*
  in the release bundle. Terminal users build from source via
  `just publish` until the CLI's distribution story stabilizes (which is
  itself a v0.1-stable concern; see ADR-0007 for the planned shape).
- **Self-contained / non-AOT artifacts.** AOT is the only build form in
  the release because (a) it works on hardware, (b) it's ~5× smaller than
  self-contained, (c) keeping the matrix at 1 keeps the workflow
  legible.
- **Code-signing, MSIX, auto-update, SBOM.** All deferred — same posture
  as ADR-0007.

## Consequences

- The releases page accumulates a `release-…` entry per push to `main`
  (one per merged PR plus any direct pushes). Users normally follow the
  fixed `/releases/latest/…` URLs, so they see only the most recent one;
  the older releases serve as an immutable audit trail of what was
  shipped, in line with GitHub's Immutable Releases setting. If the
  history grows unwieldy, a scheduled cleanup workflow can prune old
  pre-releases past N most-recent — kept out of scope for now.
- Release notes are derived purely from conventional-commit subjects.
  Commit messages are part of the user-visible deliverable, so the
  commitlint CI gate has acquired a second job: it now also gates
  release-note quality.
- SmartScreen warns on first launch — code-signing is still out of scope.
  README points users at *More info → Run anyway*.
- If `dotnet publish` flakes (rare but observed historically on
  windows-latest AOT linker steps), the rolling release simply stays at
  the previous successful commit; CI's `aot-app` job remains the
  per-PR/per-push smoke test, so the regression would surface there too.
