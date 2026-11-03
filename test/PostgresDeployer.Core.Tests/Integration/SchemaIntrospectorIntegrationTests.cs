namespace PostgresDeployer.Core.Tests.Integration;

using PostgresDeployer.Core.Helpers;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

/// <summary>
/// SchemaIntrospector 對實際 PostgreSQL 的整合測試（需設定 PGD_TEST_CONNECTION，否則略過）。
/// </summary>
public class SchemaIntrospectorIntegrationTests
{
    private const string OrdersTableSql = """
        CREATE TABLE "Orders"
        (
            "Id"     INT          NOT NULL,
            "Status" VARCHAR(20)  NOT NULL,
            "Email"  TEXT         NULL,
            CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
        );

        CREATE INDEX "IX_Orders_Open" ON "Orders" ("Id") WHERE "Status" IN ('new', 'paid');
        CREATE INDEX "IX_Orders_Email" ON "Orders" (LOWER("Email"));
        """;

    // ═══ CanonicalizeIndexesAsync ═══

    [PostgresFact]
    public async Task CanonicalizeIndexesAsync_InPredicateAndExpressionIndex_MatchIntrospectedDefinition()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ExecuteAsync(OrdersTableSql);
        var introspector = new SchemaIntrospector(db.ConnectionString);
        var desired = new SqlFileParser().ParseTableSql(OrdersTableSql);

        await introspector.CanonicalizeIndexesAsync(desired.Indexes.Select(i => (desired.TableName, i)).ToList());

        var actual = await introspector.GetTableSchemaAsync("Orders");
        foreach (var name in new[] { "IX_Orders_Open", "IX_Orders_Email" })
        {
            var desiredIdx = desired.Indexes.Single(i => i.Name == name);
            var actualIdx = actual.Indexes.Single(i => i.Name == name);
            Assert.True(IndexDefinitionComparer.Matches(desiredIdx, actualIdx),
                $"{name}: desired [{string.Join(", ", desiredIdx.Columns.Select(c => c.Name))}] WHERE {desiredIdx.Predicate} / " +
                $"actual [{string.Join(", ", actualIdx.Columns.Select(c => c.Name))}] WHERE {actualIdx.Predicate}");
        }

        // 只在交易內建立暫存物件，結束後全部回滾
        Assert.Equal(0L, await db.ScalarAsync<long>(
            "SELECT count(*) FROM pg_class WHERE relname LIKE 'pgd_canon_%'"));
    }

    [PostgresFact]
    public async Task CanonicalizeIndexesAsync_FirstIndexOfTableFails_LaterIndexesOfSameTableStillCanonicalized()
    {
        // 回歸：同表第一個索引失敗時，savepoint 回滾不可連帶移除暫存表（否則後續索引全部被靜默略過）
        await using var db = await TestDatabase.CreateAsync();
        await db.ExecuteAsync(OrdersTableSql);
        var introspector = new SchemaIntrospector(db.ConnectionString);
        var desired = new SqlFileParser().ParseTableSql(OrdersTableSql);
        var open = desired.Indexes.Single(i => i.Name == "IX_Orders_Open");
        var broken = new IndexDefinition
        {
            Name = "IX_Orders_Missing",
            Columns = [new IndexColumn { Name = "NoSuchColumn" }],
            Predicate = "\"NoSuchColumn\" > 0"
        };

        await introspector.CanonicalizeIndexesAsync([("Orders", broken), ("Orders", open)]);

        var actualIdx = (await introspector.GetTableSchemaAsync("Orders")).Indexes.Single(i => i.Name == "IX_Orders_Open");
        Assert.True(IndexDefinitionComparer.Matches(open, actualIdx), $"predicate not canonicalized: {open.Predicate}");
        Assert.Equal("\"NoSuchColumn\" > 0", broken.Predicate); // 失敗的索引維持原文
    }

    // ═══ GetViewsDependingOnColumnsAsync ═══

    [PostgresFact]
    public async Task GetViewsDependingOnColumnsAsync_ReturnsDirectAndRecursiveDependents_ExcludesViewsNotUsingColumn()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ExecuteAsync("""
            CREATE TABLE "T" ("Id" INT PRIMARY KEY, "A" VARCHAR(10), "B" VARCHAR(10));
            CREATE VIEW "v_direct"    AS SELECT "Id", "A" FROM "T";
            CREATE VIEW "v_recursive" AS SELECT "A" FROM "v_direct";
            CREATE VIEW "v_other"     AS SELECT "Id", "B" FROM "T";
            CREATE SCHEMA "rpt";
            CREATE VIEW "rpt"."v_elsewhere" AS SELECT "A" FROM "T";
            """);
        var introspector = new SchemaIntrospector(db.ConnectionString);

        var result = await introspector.GetViewsDependingOnColumnsAsync([("T", "A")]);

        Assert.Equal(["v_direct", "v_recursive"], result.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["\"T\".\"A\""], result["v_direct"]);
        Assert.Equal(["\"T\".\"A\""], result["v_recursive"]); // 間接依賴沿用上游 View 的欄位
    }

    // ═══ GetViewDependentsAsync ═══

    [PostgresFact]
    public async Task GetViewDependentsAsync_ReturnsRecursiveCrossSchemaAndMaterializedViews()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ExecuteAsync("""
            CREATE TABLE "T" ("Id" INT PRIMARY KEY, "A" VARCHAR(10));
            CREATE VIEW "v_base"  AS SELECT "Id", "A" FROM "T";
            CREATE VIEW "v_child" AS SELECT "Id" FROM "v_base";
            CREATE VIEW "v_grand" AS SELECT "Id" FROM "v_child";
            CREATE VIEW "v_unrelated" AS SELECT "Id" FROM "T";
            CREATE SCHEMA "rpt";
            CREATE VIEW "rpt"."v_report" AS SELECT "A" FROM "v_base";
            CREATE MATERIALIZED VIEW "rpt"."mv_report" AS SELECT "Id" FROM "v_base";
            """);
        var introspector = new SchemaIntrospector(db.ConnectionString);

        var result = await introspector.GetViewDependentsAsync(["v_base", "v_unrelated"]);

        var dependents = result["v_base"];
        Assert.Equal(
            [
                new DependentObject("public", "v_child", DependentObject.KindView),
                new DependentObject("public", "v_grand", DependentObject.KindView),
                new DependentObject("rpt", "mv_report", DependentObject.KindMaterializedView),
                new DependentObject("rpt", "v_report", DependentObject.KindView),
            ],
            dependents.OrderBy(d => d.Schema, StringComparer.Ordinal).ThenBy(d => d.Name, StringComparer.Ordinal));
        Assert.Contains("\"rpt\".\"mv_report\" (materialized view)", dependents.Select(d => d.ToString()));
        Assert.False(result.ContainsKey("v_unrelated"));
    }
}
