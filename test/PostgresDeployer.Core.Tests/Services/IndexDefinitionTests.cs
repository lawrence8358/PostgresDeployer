namespace PostgresDeployer.Core.Tests.Services;

using PostgresDeployer.Core.Helpers;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

/// <summary>
/// partial index / INCLUDE / USING 的解析、比對與 SQL 產生。
/// </summary>
public class IndexDefinitionTests
{
    private readonly SqlFileParser _parser = new();
    private readonly SchemaDiffer _differ = new();
    private readonly SqlGenerator _gen = new();

    private const string RepoTableSql = """
        CREATE TABLE "EIP_DevOps_Repo"
        (
            "Id"            UUID         NOT NULL,
            "ProjectId"     UUID         NOT NULL,
            "DevOpsRepoId"  VARCHAR(100) NOT NULL,
            "IsOldBranch"   BOOLEAN      NOT NULL DEFAULT FALSE,
            CONSTRAINT "PK_EIP_DevOps_Repo" PRIMARY KEY ("Id")
        );

        CREATE INDEX "IX_EIP_DevOps_Repo_ProjectId"
        ON public."EIP_DevOps_Repo"("ProjectId");

        -- 每個 Repo 只能有一列目前分支列
        CREATE UNIQUE INDEX "UIX_EIP_DevOps_Repo_ProjectId_DevOpsRepoId_Current"
        ON public."EIP_DevOps_Repo"("ProjectId", "DevOpsRepoId")
        WHERE NOT "IsOldBranch";
        """;

    // ═══ Parser ═══

    [Fact]
    public void Parse_PartialIndex_KeepsPredicateAndDefinition()
    {
        var schema = _parser.ParseTableSql(RepoTableSql);

        Assert.Equal(2, schema.Indexes.Count);
        var uix = schema.Indexes.Single(i => i.Name == "UIX_EIP_DevOps_Repo_ProjectId_DevOpsRepoId_Current");
        Assert.True(uix.IsUnique);
        Assert.Equal(["ProjectId", "DevOpsRepoId"], uix.Columns.Select(c => c.Name));
        Assert.Equal("NOT \"IsOldBranch\"", uix.Predicate);
        Assert.StartsWith("CREATE UNIQUE INDEX \"UIX_", uix.Definition);
        Assert.EndsWith("WHERE NOT \"IsOldBranch\"", uix.Definition);

        var ix = schema.Indexes.Single(i => i.Name == "IX_EIP_DevOps_Repo_ProjectId");
        Assert.Null(ix.Predicate);
        Assert.Equal(["ProjectId"], ix.Columns.Select(c => c.Name));
    }

    [Fact]
    public void Parse_UsingIncludeAndExpression_Parsed()
    {
        var sql = """
            CREATE TABLE "T" ("Id" INT NOT NULL, "Name" TEXT NULL, "Tags" JSONB NULL);
            CREATE INDEX "IX_T_Tags" ON "T" USING gin ("Tags");
            CREATE INDEX "IX_T_Name" ON "T" ("Name" DESC NULLS LAST) INCLUDE ("Id");
            CREATE INDEX "IX_T_Lower" ON "T" (lower("Name"));
            """;

        var schema = _parser.ParseTableSql(sql);

        var gin = schema.Indexes.Single(i => i.Name == "IX_T_Tags");
        Assert.Equal("gin", gin.Method);

        var named = schema.Indexes.Single(i => i.Name == "IX_T_Name");
        Assert.Equal("btree", named.Method);
        Assert.True(named.Columns[0].IsDescending);
        Assert.Equal("Name", named.Columns[0].Name);
        Assert.Equal(["Id"], named.IncludeColumns);

        var lower = schema.Indexes.Single(i => i.Name == "IX_T_Lower");
        Assert.True(lower.Columns[0].IsExpression);
        Assert.Equal("lower(\"Name\")", lower.Columns[0].Name);
    }

    [Fact]
    public void Parse_CommentedOutIndex_Ignored()
    {
        var sql = """
            CREATE TABLE "T" ("Id" INT NOT NULL);
            -- CREATE INDEX "IX_Old" ON "T" ("Id");
            /* CREATE INDEX "IX_Old2" ON "T" ("Id"); */
            CREATE INDEX "IX_T_Id" ON "T" ("Id");
            """;

        var schema = _parser.ParseTableSql(sql);

        Assert.Equal(["IX_T_Id"], schema.Indexes.Select(i => i.Name));
    }

    [Fact]
    public void Parse_MissingSemicolon_ThrowsWithLineNumber()
    {
        var sql = """
            CREATE TABLE "T"
            (
                "Id" INT NOT NULL
            );

            CREATE INDEX "IX_T_Id" ON "T" ("Id")

            COMMENT ON TABLE "T" IS 'test';
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => _parser.ParseTableSql(sql));
        Assert.Contains("8", ex.Message);
        Assert.Contains("COMMENT ON", ex.Message);
    }

    // ═══ Differ ═══

    private static TableSchema ActualRepoTable(string? uixPredicate)
    {
        var table = new TableSchema { TableName = "EIP_DevOps_Repo" };
        table.Columns.AddRange(
        [
            new ColumnDefinition { Name = "Id", Type = "UUID", RawType = "UUID", IsNullable = false },
            new ColumnDefinition { Name = "ProjectId", Type = "UUID", RawType = "UUID", IsNullable = false },
            new ColumnDefinition { Name = "DevOpsRepoId", Type = "VARCHAR", RawType = "VARCHAR(100)", Length = 100, IsNullable = false },
            new ColumnDefinition { Name = "IsOldBranch", Type = "BOOLEAN", RawType = "BOOLEAN", IsNullable = false, HasDefault = true, DefaultValue = "false" },
        ]);
        table.PrimaryKey = new PrimaryKeyDefinition { ConstraintName = "PK_EIP_DevOps_Repo", Columns = ["Id"] };
        table.Indexes.Add(new IndexDefinition
        {
            Name = "IX_EIP_DevOps_Repo_ProjectId",
            Columns = [new IndexColumn { Name = "ProjectId" }]
        });
        if (uixPredicate != null)
        {
            table.Indexes.Add(new IndexDefinition
            {
                Name = "UIX_EIP_DevOps_Repo_ProjectId_DevOpsRepoId_Current",
                IsUnique = true,
                Columns = [new IndexColumn { Name = "ProjectId" }, new IndexColumn { Name = "DevOpsRepoId" }],
                Predicate = uixPredicate
            });
        }
        return table;
    }

    [Fact]
    public void Diff_PartialIndexSameAsDatabase_NoIndexChange()
    {
        var desired = _parser.ParseTableSql(RepoTableSql);
        var actual = new Dictionary<string, TableSchema>
        {
            ["EIP_DevOps_Repo"] = ActualRepoTable("(NOT \"IsOldBranch\")")
        };

        var changes = _differ.ComputeChanges([desired], actual);

        Assert.DoesNotContain(changes, c => c.Type is ChangeType.CreateIndex or ChangeType.RecreateIndex or ChangeType.DropIndex);
    }

    [Fact]
    public void Diff_PartialIndexMissing_CreateChangeCarriesExactIndexNameAndCaution()
    {
        var desired = _parser.ParseTableSql(RepoTableSql);
        var actual = new Dictionary<string, TableSchema>
        {
            ["EIP_DevOps_Repo"] = ActualRepoTable(null)
        };

        var change = Assert.Single(_differ.ComputeChanges([desired], actual),
            c => c.Type == ChangeType.CreateIndex);

        Assert.Equal("UIX_EIP_DevOps_Repo_ProjectId_DevOpsRepoId_Current", change.ColumnName);
        Assert.NotNull(change.CautionMessage);
    }

    [Fact]
    public void Diff_PredicateChanged_ReturnsRecreateIndex()
    {
        var desired = _parser.ParseTableSql(RepoTableSql);
        var actual = new Dictionary<string, TableSchema>
        {
            ["EIP_DevOps_Repo"] = ActualRepoTable("\"IsOldBranch\"")
        };

        var change = Assert.Single(_differ.ComputeChanges([desired], actual),
            c => c.Type == ChangeType.RecreateIndex);
        Assert.Equal("UIX_EIP_DevOps_Repo_ProjectId_DevOpsRepoId_Current", change.ColumnName);
    }

    [Fact]
    public void IndexDefinitionComparer_DifferentMethodOrInclude_ReturnsFalse()
    {
        var a = new IndexDefinition { Name = "IX", Columns = [new IndexColumn { Name = "A" }] };
        var b = new IndexDefinition { Name = "IX", Columns = [new IndexColumn { Name = "A" }], Method = "hash" };
        var c = new IndexDefinition { Name = "IX", Columns = [new IndexColumn { Name = "A" }], IncludeColumns = ["B"] };

        Assert.False(IndexDefinitionComparer.Matches(a, b));
        Assert.False(IndexDefinitionComparer.Matches(a, c));
        Assert.True(IndexDefinitionComparer.Matches(a, new IndexDefinition { Name = "IX", Columns = [new IndexColumn { Name = "A" }] }));
    }

    // ═══ Generator ═══

    [Fact]
    public void GenerateCreateIndex_WithDefinition_UsesDefinitionVerbatim()
    {
        var uix = _parser.ParseTableSql(RepoTableSql).Indexes
            .Single(i => i.Name.StartsWith("UIX_", StringComparison.Ordinal));

        var sql = _gen.GenerateCreateIndex("EIP_DevOps_Repo", uix);

        Assert.Equal(uix.Definition + ";", sql);
        Assert.Contains("WHERE NOT \"IsOldBranch\"", sql);
    }

    [Fact]
    public void GenerateCreateIndex_StructuredPartialIndex_IncludesUsingIncludeWhere()
    {
        var idx = new IndexDefinition
        {
            Name = "IX_T",
            Method = "gist",
            Columns = [new IndexColumn { Name = "Geom" }],
            IncludeColumns = ["Id"],
            Predicate = "\"Geom\" IS NOT NULL"
        };

        var sql = _gen.GenerateCreateIndex("T", idx);

        Assert.Equal(
            "CREATE INDEX IF NOT EXISTS \"IX_T\" ON \"T\" USING gist (\"Geom\") INCLUDE (\"Id\") WHERE \"Geom\" IS NOT NULL;",
            sql);
    }

    [Theory]
    [InlineData(
        "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_T_A\" ON \"T\" (\"A\")",
        "CREATE INDEX IF NOT EXISTS \"IX_T_A\" ON \"T\" (\"A\");")]
    [InlineData(
        "create unique index concurrently \"UIX_T_A\" on \"T\" (\"A\") WHERE \"A\" > 0",
        "create unique index \"UIX_T_A\" on \"T\" (\"A\") WHERE \"A\" > 0;")]
    [InlineData(
        "CREATE INDEX /* 線上建立 */ CONCURRENTLY \"IX_T_A\" ON \"T\" (\"A\")",
        "CREATE INDEX /* 線上建立 */ \"IX_T_A\" ON \"T\" (\"A\");")]
    public void GenerateCreateIndex_DefinitionWithConcurrently_RemovesKeyword(string definition, string expected)
    {
        // 部署群組在交易內執行，CONCURRENTLY 會失敗（cannot run inside a transaction block）
        var idx = new IndexDefinition { Name = "IX", Definition = definition };

        Assert.Equal(expected, _gen.GenerateCreateIndex("T", idx));
    }

    [Theory]
    [InlineData("CREATE INDEX \"CONCURRENTLY\" ON \"T\" (\"A\")")]
    [InlineData("CREATE INDEX \"IX_T_A\" ON \"T\" (\"A\") WHERE \"Note\" <> 'CONCURRENTLY'")]
    [InlineData("-- CREATE INDEX CONCURRENTLY\nCREATE INDEX \"IX_T_A\" ON \"T\" (\"A\")")]
    public void GenerateCreateIndex_ConcurrentlyInIdentifierStringOrComment_Unchanged(string definition)
    {
        var idx = new IndexDefinition { Name = "IX", Definition = definition };

        Assert.Equal(definition + ";", _gen.GenerateCreateIndex("T", idx));
    }
}
