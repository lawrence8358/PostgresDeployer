namespace PostgresDeployer.Core.Tests.Helpers;

using PostgresDeployer.Core.Helpers;

public class SqlTextScannerTests
{
    [Fact]
    public void Mask_BlanksCommentsStringsAndDollarQuotes_KeepsLength()
    {
        var sql = "SELECT 'a;b' -- x;y\n, \"c;d\" /* e;f */, $$g;h$$;";
        var masked = SqlTextScanner.Mask(sql);

        Assert.Equal(sql.Length, masked.Length);
        Assert.Equal(1, masked.Count(c => c == ';'));
        Assert.EndsWith(";", masked);
        Assert.Contains('\n', masked);
    }

    [Fact]
    public void SplitStatements_IgnoresSemicolonsInCommentsAndStrings()
    {
        var sql = "-- lead; comment\nCREATE TABLE \"T\" (\"A\" INT);\n\nCOMMENT ON TABLE \"T\" IS 'x;y';\n";
        var stmts = SqlTextScanner.SplitStatements(sql);

        Assert.Equal(2, stmts.Count);
        Assert.Equal("CREATE TABLE \"T\" (\"A\" INT)", stmts[0].Text);
        Assert.Equal("COMMENT ON TABLE \"T\" IS 'x;y'", stmts[1].Text);
        Assert.Equal(sql.IndexOf("CREATE", StringComparison.Ordinal), stmts[0].Start);
    }

    [Fact]
    public void FindMissingTerminator_IndexWithoutSemicolon_ReturnsNextStatementLine()
    {
        var sql = """
            CREATE TABLE "T"
            (
                "Id" INT NOT NULL
            );

            CREATE INDEX "IX_T_Id" ON "T" ("Id")

            COMMENT ON TABLE "T" IS 'test';
            """;

        var result = SqlTextScanner.FindMissingTerminator(sql);

        Assert.NotNull(result);
        Assert.Equal("COMMENT ON", result!.Value.Keyword);
        Assert.Equal(8, SqlTextScanner.GetLineNumber(sql, result.Value.Offset));
    }

    [Fact]
    public void FindMissingTerminator_TableWithoutSemicolon_Detected()
    {
        var sql = "CREATE TABLE \"T\"\n(\n    \"Id\" INT\n)\nCREATE INDEX \"IX\" ON \"T\" (\"Id\");";

        var result = SqlTextScanner.FindMissingTerminator(sql);

        Assert.NotNull(result);
        Assert.Equal("CREATE", result!.Value.Keyword);
        Assert.Equal(5, SqlTextScanner.GetLineNumber(sql, result.Value.Offset));
    }

    [Fact]
    public void FindMissingTerminator_WellFormedFile_ReturnsNull()
    {
        var sql = """
            CREATE TABLE "T"
            (
                "Id"    INT NOT NULL,
                "Pid"   INT NULL,
                CONSTRAINT "FK_T_Pid" FOREIGN KEY ("Pid") REFERENCES "P"("Id")
                    ON DELETE CASCADE
            );

            -- 註解中的
            -- CREATE INDEX "IX_Old" ON "T" ("Id")
            CREATE UNIQUE INDEX "UIX_T"
            ON public."T"("Id")
            WHERE "Pid" IS NULL;

            COMMENT ON COLUMN "T"."Id" IS '多行
            CREATE 不是陳述式';
            """;

        Assert.Null(SqlTextScanner.FindMissingTerminator(sql));
    }

    [Theory]
    [InlineData("CREATE RULE")]
    [InlineData("CREATE OR REPLACE RULE")]
    [InlineData("create or replace rule")]
    public void FindMissingTerminator_RuleWithActionOnNewLine_ReturnsNull(string createRule)
    {
        // RULE 的動作（DO ... INSERT INTO）換行寫在行首是合法的，不是漏分號
        var sql = $"""
            CREATE TABLE "T"
            (
                "Id" INT NOT NULL
            );

            {createRule} "R_T_Log" AS ON INSERT TO "T"
            DO ALSO
            INSERT INTO "T_Log" ("Id") VALUES (NEW."Id");
            """;

        Assert.Null(SqlTextScanner.FindMissingTerminator(sql));
    }

    [Fact]
    public void FindMissingTerminator_StatementAfterRuleWithoutSemicolon_StillDetected()
    {
        // 只略過 RULE 陳述式本身；之後的陳述式漏分號仍要偵測
        var sql = "CREATE RULE \"R\" AS ON INSERT TO \"T\"\nDO INSTEAD NOTHING;\nCREATE INDEX \"IX\" ON \"T\" (\"Id\")\nCOMMENT ON TABLE \"T\" IS 'x';";

        var result = SqlTextScanner.FindMissingTerminator(sql);

        Assert.NotNull(result);
        Assert.Equal("COMMENT ON", result!.Value.Keyword);
    }
}
