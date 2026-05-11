using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Linerule.XTask;

/// <summary>
/// Banned-regex sweep over <c>**/*.cs</c>. Patterns capture known bug-source idioms
/// that the analyzer suite alone won't reject. Per memory <c>feedback_defensive_gates_upfront</c>:
/// bug sources are stopped at <c>just lint</c>, not "caught in review".
/// </summary>
internal static class StrictCodeCommand
{
    // NonBacktracking is intentionally NOT included — the bare-tracking-comment
    // rule uses a negative lookahead which the NonBacktracking engine refuses.
    // PatternTimeout (2 s) bounds the runaway risk on traditional backtracking.
    private const RegexOptions BaseOptions = RegexOptions.Compiled | RegexOptions.ExplicitCapture;
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    public static Command Build()
    {
        var cmd = new Command("strict-code", "Banned-regex sweep over the source tree.");
        cmd.SetAction(_ => Run(Directory.GetCurrentDirectory()));
        return cmd;
    }

    public static int Run(string repoRoot)
    {
        var rules = BuildRules();
        var violations = new List<Violation>();

        var sources = EnumerateSources(repoRoot).Where(file => !IsExcluded(Path.GetRelativePath(repoRoot, file)));

        foreach (var file in sources)
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                violations.AddRange(
                    rules
                        .Where(rule => rule.Pattern.IsMatch(line))
                        .Select(rule => new Violation(rel, i + 1, line.TrimStart(), rule.Name, rule.Reason))
                );
            }
        }

        if (violations.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]strict-code:[/] OK ([grey]0 violations[/])");
            return 0;
        }

        var table = new Table().AddColumns("file", "line", "rule", "snippet");
        foreach (var v in violations)
        {
            table.AddRow(
                $"[yellow]{v.Path}[/]",
                v.Line.ToString(CultureInfo.InvariantCulture),
                $"[red]{v.Rule}[/]",
                Markup.Escape(Truncate(v.Snippet, 80))
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated($"[red]strict-code:[/] {violations.Count} violation(s)");
        foreach (var rule in rules)
        {
            AnsiConsole.MarkupLineInterpolated($"  [grey]{rule.Name}[/]: {rule.Reason}");
        }

        return 1;
    }

    private static IReadOnlyList<Rule> BuildRules() =>
        [
            new(
                "ban-dynamic",
                new Regex(@"\bdynamic\s+\w", BaseOptions, PatternTimeout),
                "`dynamic` is AOT/trim-incompatible. Use generics or interfaces."
            ),
            new(
                "ban-goto",
                new Regex(@"^\s*goto\s+\w", BaseOptions, PatternTimeout),
                "`goto` is forbidden — restructure with helpers or break/continue."
            ),
            new(
                "ban-pragma-disable",
                new Regex(@"#pragma\s+warning\s+disable", BaseOptions, PatternTimeout),
                "Fix the warning at its root, do not silence it."
            ),
            new(
                "ban-suppress-message",
                new Regex(@"\[SuppressMessage\b", BaseOptions, PatternTimeout),
                "Replace with a code fix, or use the fixed Justification escape hatch documented in ADR-0006."
            ),
            new(
                "ban-bare-todo",
                new Regex(@"//\s*(?:TODO|FIXME|XXX)(?!.*[#A-Z]+-?\d)", BaseOptions, PatternTimeout),
                "Reference an issue (e.g. `// TODO #42 …`)."
            ),
            new(
                "ban-newtonsoft",
                new Regex(@"\bNewtonsoft\.Json\b", BaseOptions, PatternTimeout),
                "Use System.Text.Json source generators."
            ),
            new(
                "ban-reflection-emit",
                new Regex(@"\bSystem\.Reflection\.Emit\b", BaseOptions, PatternTimeout),
                "Reflection.Emit is AOT-incompatible — use source generators."
            ),
            new(
                "ban-activator-type",
                new Regex(@"\bActivator\.CreateInstance\s*\(\s*typeof", BaseOptions, PatternTimeout),
                "Trim/AOT unsafe; use a typed factory."
            ),
            new(
                "ban-sql-interpolation",
                new Regex("CommandText\\s*=\\s*\\$\"", BaseOptions, PatternTimeout),
                "Interpolated SQL is an injection vector — bind via DbCommand.Parameters or use static SQL with placeholders."
            ),
        ];

    private static IEnumerable<string> EnumerateSources(string root)
    {
        foreach (var dir in new[] { "src", "tests" })
        {
            var path = Path.Combine(root, dir);
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
                {
                    yield return f;
                }
            }
        }
    }

    private static bool IsExcluded(string relativePath)
    {
        // Generated artifacts and the xtask itself (the rules quote the patterns
        // they ban, which would otherwise self-trigger).
        var normalized = relativePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/Generated/", StringComparison.Ordinal)
            || normalized.EndsWith(".g.cs", StringComparison.Ordinal)
            || normalized.Contains("Linerule.XTask/StrictCodeCommand.cs", StringComparison.Ordinal);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed record Rule(string Name, Regex Pattern, string Reason);

    private sealed record Violation(string Path, int Line, string Snippet, string Rule, string Reason);
}
