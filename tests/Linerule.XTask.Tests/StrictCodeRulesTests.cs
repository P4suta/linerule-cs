using System;
using System.IO;
using Linerule.XTask;

namespace Linerule.XTask.Tests;

/// <summary>
/// Per-rule fixture tests for <see cref="StrictCodeCommand"/>. Each rule has
/// a positive case (offending source → exit 1) and the suite shares a
/// negative case (clean source → exit 0). Fixtures live under
/// <see cref="Directory.CreateTempSubdirectory(string)"/> so the strict-code
/// CI sweep doesn't trip over the fixtures themselves.
/// </summary>
public sealed class StrictCodeRulesTests
{
    [Theory]
    // Code hygiene
    [InlineData("ban-goto", "        goto label;")]
    [InlineData("ban-pragma-disable", "#pragma warning disable CS1591")]
    [InlineData("ban-suppress-message", "[SuppressMessage(\"x\", \"y\")]")]
    [InlineData("ban-bare-todo", "// TODO clean this up")]
    [InlineData("ban-newtonsoft", "using Newtonsoft.Json;")]
    // AOT / trim reflection vectors
    [InlineData("ban-dynamic", "dynamic value = 1;")]
    [InlineData("ban-reflection-emit", "var x = typeof(System.Reflection.Emit.AssemblyBuilder);")]
    [InlineData("ban-activator-type", "Activator.CreateInstance(typeof(Foo));")]
    [InlineData("ban-make-generic-type", "var t = typeof(List<>).MakeGenericType(typeof(int));")]
    [InlineData("ban-make-generic-method", "var m = method.MakeGenericMethod(typeof(int));")]
    [InlineData("ban-type-gettype-string", "var t = Type.GetType(\"System.Int32\");")]
    [InlineData("ban-assembly-load", "Assembly.Load(\"Foo\");")]
    [InlineData("ban-reflection-member-lookup", "var p = obj.GetType().GetProperty(\"X\");")]
    // SQL injection vector
    [InlineData("ban-sql-interpolation", "cmd.CommandText = $\"SELECT * FROM {t}\";")]
    public void Positive_each_rule_returns_1(string ruleName, string offendingLine)
    {
        _ = ruleName; // surfaced in test name via [InlineData]; not consumed by Run
        var tmp = Directory.CreateTempSubdirectory("xtask-strict-pos-");
        try
        {
            WriteFixture(tmp.FullName, offendingLine);
            Assert.Equal(1, StrictCodeCommand.Run(tmp.FullName));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Negative_clean_source_returns_0()
    {
        var tmp = Directory.CreateTempSubdirectory("xtask-strict-neg-");
        try
        {
            WriteFixture(tmp.FullName, "var x = 42;");
            Assert.Equal(0, StrictCodeCommand.Run(tmp.FullName));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Empty_tree_returns_0()
    {
        // No src/ or tests/ subdirectory — EnumerateSources yields nothing.
        var tmp = Directory.CreateTempSubdirectory("xtask-strict-empty-");
        try
        {
            Assert.Equal(0, StrictCodeCommand.Run(tmp.FullName));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Excluded_paths_are_skipped()
    {
        // Generated and bin/obj files should NOT trip rules. Place an offending
        // line under each excluded path; expect exit 0.
        var tmp = Directory.CreateTempSubdirectory("xtask-strict-excl-");
        try
        {
            foreach (var subdir in new[] { "src/obj", "src/bin", "src/Generated" })
            {
                var dir = Path.Combine(tmp.FullName, subdir);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "Excluded.cs"), "dynamic value = 1;");
            }
            // *.g.cs extension exclusion check (anywhere under src/).
            File.WriteAllText(Path.Combine(tmp.FullName, "src", "Trip.g.cs"), "dynamic value = 1;");

            Assert.Equal(0, StrictCodeCommand.Run(tmp.FullName));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Tests_directory_is_also_swept()
    {
        // EnumerateSources walks both src/ and tests/. A violation under tests/
        // must trip the rule.
        var tmp = Directory.CreateTempSubdirectory("xtask-strict-tests-");
        try
        {
            var dir = Path.Combine(tmp.FullName, "tests");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Bad.cs"), "dynamic value = 1;");
            Assert.Equal(1, StrictCodeCommand.Run(tmp.FullName));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    private static void WriteFixture(string root, string offendingLine)
    {
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        // Minimum-valid C# scaffolding — StrictCodeCommand reads lines only,
        // no compilation is involved, but a realistic shape keeps fixtures
        // honest if rules ever evolve to use Roslyn.
        var body = $"class Foo {{\n    void M() {{\n        {offendingLine}\n    }}\n}}\n";
        File.WriteAllText(Path.Combine(src, "Foo.cs"), body);
    }
}
