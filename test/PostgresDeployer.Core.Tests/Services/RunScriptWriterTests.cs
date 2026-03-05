namespace PostgresDeployer.Core.Tests.Services;

using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public class RunScriptWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RunScriptWriter _writer = new();

    public RunScriptWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static DeployPlan MakePlan(params (string GroupName, string[] Statements)[] groups)
    {
        var plan = new DeployPlan();
        foreach (var (name, stmts) in groups)
        {
            var g = new DeployGroup { Name = name };
            g.Statements.AddRange(stmts);
            plan.Groups.Add(g);
        }
        return plan;
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Write_CreatesRunScriptsDirectory()
    {
        var plan = MakePlan(("Extensions", ["CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";"]));
        var ts = new DateTime(2026, 3, 4, 15, 22, 33);

        _writer.Write(plan, "localhost:5432/TestDb", _tempDir, ts);

        Assert.True(Directory.Exists(Path.Combine(_tempDir, "RunScripts")));
    }

    [Fact]
    public void Write_ReturnsCorrectFilePath()
    {
        var plan = MakePlan(("Extensions", ["CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";"]));
        var ts = new DateTime(2026, 3, 4, 15, 22, 33);

        var path = _writer.Write(plan, "localhost:5432/TestDb", _tempDir, ts);

        var expectedPath = Path.Combine(_tempDir, "RunScripts", "20260304152233.sql");
        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void Write_FileNameMatchesTimestamp()
    {
        var plan = MakePlan(("Extensions", ["SELECT 1;"]));
        var ts = new DateTime(2025, 12, 31, 8, 5, 9);

        var path = _writer.Write(plan, "host:5432/db", _tempDir, ts);

        Assert.Equal("20251231080509.sql", Path.GetFileName(path));
    }

    [Fact]
    public void Write_HeaderContainsTimestampAndHost()
    {
        var plan = MakePlan(("Extensions", ["SELECT 1;"]));
        var ts = new DateTime(2026, 3, 4, 15, 22, 33);

        var path = _writer.Write(plan, "localhost:5432/MyDb", _tempDir, ts);
        var content = File.ReadAllText(path);

        Assert.Contains("-- Generated: 2026-03-04 15:22:33", content);
        Assert.Contains("-- Host:      localhost:5432/MyDb", content);
    }

    [Fact]
    public void Write_ContainsGroupNameAndSql()
    {
        var plan = MakePlan(
            ("Extensions", ["CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";"]),
            ("New Tables", ["CREATE TABLE \"users\" (\"id\" UUID PRIMARY KEY);"])
        );

        var path = _writer.Write(plan, "localhost:5432/TestDb", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("-- [Group: Extensions]", content);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";", content);
        Assert.Contains("-- [Group: New Tables]", content);
        Assert.Contains("CREATE TABLE \"users\"", content);
    }

    [Fact]
    public void Write_StatementsAppearInOrder()
    {
        var plan = MakePlan(
            ("A", ["SQL_A1;", "SQL_A2;"]),
            ("B", ["SQL_B1;"])
        );

        var path = _writer.Write(plan, "host:5432/db", _tempDir);
        var content = File.ReadAllText(path);

        var posA1 = content.IndexOf("SQL_A1;", StringComparison.Ordinal);
        var posA2 = content.IndexOf("SQL_A2;", StringComparison.Ordinal);
        var posB1 = content.IndexOf("SQL_B1;", StringComparison.Ordinal);

        Assert.True(posA1 < posA2, "SQL_A1 should appear before SQL_A2");
        Assert.True(posA2 < posB1, "Group A statements should appear before Group B");
    }

    [Fact]
    public void Write_EmptyPlan_OnlyWritesHeader()
    {
        var plan = new DeployPlan(); // No groups

        var path = _writer.Write(plan, "host:5432/db", _tempDir);
        var content = File.ReadAllText(path);

        Assert.Contains("-- PostgresDeployer RunScript", content);
        Assert.DoesNotContain("-- [Group:", content);
    }

    [Fact]
    public void Write_EmptyGroupsSkipped()
    {
        var plan = MakePlan(
            ("EmptyGroup", []),          // empty group — should be skipped
            ("Has SQL", ["SELECT 1;"])
        );

        var path = _writer.Write(plan, "host:5432/db", _tempDir);
        var content = File.ReadAllText(path);

        Assert.DoesNotContain("-- [Group: EmptyGroup]", content);
        Assert.Contains("-- [Group: Has SQL]", content);
    }

    [Fact]
    public void Write_SqlWithoutSemicolon_AppendsSemicolon()
    {
        // SQL without trailing semicolon
        var plan = MakePlan(("Test", ["SELECT 1"]));

        var path = _writer.Write(plan, "host:5432/db", _tempDir);
        var content = File.ReadAllText(path);

        var idx = content.IndexOf("SELECT 1", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Expected 'SELECT 1' to appear in the file");

        // Find the next semicolon after "SELECT 1"
        var semiIdx = content.IndexOf(';', idx);
        Assert.True(semiIdx >= 0, "Expected a semicolon to appear after 'SELECT 1'");
    }

    [Fact]
    public void Write_UsesNowWhenTimestampNotProvided()
    {
        var plan = MakePlan(("Test", ["SELECT 1;"]));
        var before = DateTime.Now;

        var path = _writer.Write(plan, "host:5432/db", _tempDir);

        var after = DateTime.Now;
        var fileName = Path.GetFileNameWithoutExtension(path); // yyyyMMddHHmmss
        var ts = DateTime.ParseExact(fileName, "yyyyMMddHHmmss", null);

        Assert.True(ts >= before.AddSeconds(-1) && ts <= after.AddSeconds(1));
    }
}
