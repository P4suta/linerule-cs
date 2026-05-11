using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Linerule.Diagnostics.Storage;
using Microsoft.Data.Sqlite;

namespace Linerule.Diagnostics.Storage.Tests;

/// <summary>
/// Schema-shape contract. The migrator is idempotent, every DDL clause is
/// <c>CREATE … IF NOT EXISTS</c>, so the invariant we pin here is the set
/// of named objects sqlite_master enumerates after one open. Drift in
/// <c>schema.sql</c> that drops or renames an index will trip these
/// before it gets a chance to silently degrade query plans in
/// production.
/// </summary>
public sealed partial class SchemaTests
{
    [Fact]
    public void Opens_fresh_db_and_creates_expected_tables_and_indices()
    {
        using var temp = new TempDb();

        using (var sink = new SqliteEventSink(temp.Path, RunMetadata.Capture(["test"])))
        {
            // Constructing the sink applies the schema and inserts the
            // runs row; we don't need to write any events for the
            // sqlite_master snapshot.
        }

        using var conn = new SqliteConnection($"Data Source={temp.Path};Mode=ReadOnly;");
        conn.Open();

        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                tables.Add(rdr.GetString(0));
            }
        }
        Assert.Contains("events", tables);
        Assert.Contains("runs", tables);

        var indices = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'index' AND name LIKE 'idx_%' ORDER BY name;";
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                indices.Add(rdr.GetString(0));
            }
        }
        Assert.Contains("idx_events_run", indices);
        Assert.Contains("idx_events_sub_ts", indices);
        Assert.Contains("idx_events_lvl_ts", indices);
        Assert.Contains("idx_events_activity", indices);
        Assert.Contains("idx_runs_started", indices);
    }

    [Fact]
    public void Tables_are_declared_strict()
    {
        // STRICT-typed tables are the schema-layer invariant that makes
        // column-affinity bugs hard errors at insert time. sqlite_master
        // exposes the raw CREATE TABLE text; we pattern-match for the
        // STRICT trailer.
        using var temp = new TempDb();
        using (var sink = new SqliteEventSink(temp.Path, RunMetadata.Capture(["test"])))
        {
            // schema-only assertion; nothing to write.
        }

        using var conn = new SqliteConnection($"Data Source={temp.Path};Mode=ReadOnly;");
        conn.Open();
        foreach (var t in new[] { "events", "runs" })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name;";
            cmd.Parameters.AddWithValue("$name", t);
            var ddl = cmd.ExecuteScalar() as string;
            Assert.NotNull(ddl);
            Assert.Contains("STRICT", ddl, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The 16-byte invariant on <c>runs.run_id</c> (and the same shape on
    /// <c>events.run_id</c> / <c>events.session_id</c>) is encoded as
    /// <c>CHECK (length(...) = 16)</c> because SQLite STRICT mode rejects
    /// the column-modifier <c>BLOB(16)</c> syntax. A short blob must
    /// fail at INSERT, a 16-byte blob must succeed.
    /// </summary>
    [Fact]
    public void Length_constraint_rejects_non_16_byte_blob()
    {
        using var temp = new TempDb();
        using (var _ = new SqliteEventSink(temp.Path, RunMetadata.Capture(["len-check"])))
        {
            // Initialize the schema; we'll exercise the runs CHECK
            // directly against the file below.
        }

        using var conn = new SqliteConnection($"Data Source={temp.Path};");
        conn.Open();

        // 1-byte blob — must trip the CHECK.
        var shortInsert = SqliteException_FromInsert(conn, [0x00]);
        Assert.NotNull(shortInsert);
        Assert.Contains("CHECK constraint failed", shortInsert!.Message, StringComparison.OrdinalIgnoreCase);

        // 16-byte blob — must succeed.
        var ok = SqliteException_FromInsert(
            conn,
            [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15]
        );
        Assert.Null(ok);
    }

    private static SqliteException? SqliteException_FromInsert(SqliteConnection conn, byte[] runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO runs (run_id, started_at_utc, version, build_config, args, hostname, pid) "
            + "VALUES ($id, '2026-01-01T00:00:00Z', 't', 'Debug', '', 'h', 0);";
        cmd.Parameters.AddWithValue("$id", runId);
        try
        {
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (SqliteException ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// The const-string DDL in <see cref="SqliteSchema"/> is the runtime
    /// authority, but the sibling <c>schema.sql</c> file is checked in as
    /// human-readable documentation. This test pins the two together so
    /// drift surfaces in CI: every statement in <c>schema.sql</c> must
    /// appear in <see cref="SqliteSchema.StatementsForTest"/> (whitespace
    /// canonicalized), and vice versa. The comparison is order-agnostic
    /// because the two sources organize the statements differently
    /// (.sql groups by section, C# groups by execution).
    /// </summary>
    [Fact]
    public void Sql_file_matches_inline_statements()
    {
        var sqlPath = LocateSchemaSql();
        Assert.True(File.Exists(sqlPath), $"schema.sql not found at {sqlPath}");
        var sqlText = File.ReadAllText(sqlPath);

        var fileStatements = SplitAndCanonicalise(sqlText).ToHashSet(StringComparer.Ordinal);
        var inlineStatements = SqliteSchema.StatementsForTest().Select(Canonicalise).ToHashSet(StringComparer.Ordinal);

        var onlyInFile = fileStatements.Except(inlineStatements, StringComparer.Ordinal).ToList();
        var onlyInline = inlineStatements.Except(fileStatements, StringComparer.Ordinal).ToList();

        Assert.True(
            onlyInFile.Count == 0 && onlyInline.Count == 0,
            "schema.sql and SqliteSchema.StatementsForTest drifted.\n"
                + $"Only in schema.sql ({onlyInFile.Count}):\n  "
                + string.Join("\n  ", onlyInFile)
                + "\n"
                + $"Only inline ({onlyInline.Count}):\n  "
                + string.Join("\n  ", onlyInline)
        );
    }

    private static IEnumerable<string> SplitAndCanonicalise(string sqlText)
    {
        // Strip `-- …` comment tails per line, then split on `;`. The
        // schema file uses only line comments and no semicolons inside
        // string literals, so naive splitting suffices.
        var lines = sqlText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf("--", StringComparison.Ordinal);
            if (idx >= 0)
            {
                lines[i] = lines[i][..idx];
            }
        }
        var stripped = string.Join('\n', lines);
        foreach (var raw in stripped.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var canonical = Canonicalise(raw);
            if (canonical.Length > 0)
            {
                yield return canonical;
            }
        }
    }

    private static string Canonicalise(string sql) => WhitespaceRun.Replace(sql, " ").Trim();

    private static string LocateSchemaSql()
    {
        // Test binaries live under
        //   tests/Linerule.Diagnostics.Storage.Tests/bin/<Config>/<Tfm>/
        // so the source file is four levels up + into src/. We walk up
        // until we find the repo root marker so the test is resilient to
        // small layout shifts.
        var dir = System.AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "Linerule.Diagnostics.Storage", "schema.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "Could not locate src/Linerule.Diagnostics.Storage/schema.sql by walking up from "
                + System.AppContext.BaseDirectory
        );
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRun { get; }
}
