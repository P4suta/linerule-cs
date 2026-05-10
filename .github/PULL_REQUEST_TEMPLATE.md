## Summary

<!-- One paragraph: what changed and why. PR title follows Conventional Commits (`feat:` / `fix:` / `docs:` / `refactor:` / etc). -->

## Related ADR / issue

<!-- ADR number(s) this affects, e.g. ADR-0002, or "n/a". Link to the issue when present. -->

## How was this verified?

- [ ] `just lint` (csharpier + dotnet format + typos + actionlint + xtask strict-code)
- [ ] `just test` (unit + property + snapshot)
- [ ] `just ci` (full local replica)
- [ ] Manual smoke (Windows): _which mode / hotkey / config edit?_

## Architecture beauty checklist

- [ ] No new `_ => throw` arms in closed-DU switches.
- [ ] No `[SuppressMessage]` / `#pragma warning disable` / `<NoWarn>`.
- [ ] No reflection / `dynamic` / `Activator.CreateInstance(Type)`.
- [ ] Newtypes constructed via `TryCreate`, not directly.
- [ ] Phantom-typed coords kept distinct.

## Notes for reviewers

<!-- Anything subtle, plus a pointer to the bit that was hardest to design. -->
