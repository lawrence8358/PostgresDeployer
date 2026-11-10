namespace PostgresDeployer.Core.Tests.Integration;

using Microsoft.Extensions.Logging.Abstractions;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

/// <summary>
/// DeployOrchestrator.AnalyzeAsync 對實際 PostgreSQL 的端對端測試（需設定 PGD_TEST_CONNECTION，否則略過）。
/// </summary>
public class DeployOrchestratorIntegrationTests
{
    private const string PreDropGroupName = "Drop Views (Pre-Alter)";

    private static DeployOrchestrator CreateOrchestrator() => new(NullLogger<DeployOrchestrator>.Instance);

    /// <summary>
    /// DB：Customer("Name" VARCHAR(50))、v_customer（schema 目錄中）、v_external（schema 目錄外，依賴 v_customer）。
    /// schema 目錄：Customer 的 "Name" 改為 VARCHAR(100)，v_customer 欄位不變。
    /// v_customer 引用 "Name" → 必須 Pre-Drop → CASCADE 會刪除 v_external。
    /// </summary>
    private static async Task ArrangeAlterTypeWithExternalDependentAsync(TestDatabase db, TempSchemaDirectory dir)
    {
        await db.ExecuteAsync("""
            CREATE TABLE "Customer" ("Id" INT NOT NULL, "Name" VARCHAR(50) NULL, CONSTRAINT "PK_Customer" PRIMARY KEY ("Id"));
            CREATE VIEW "v_customer" AS SELECT "Id", "Name" FROM "Customer";
            CREATE VIEW "v_external" AS SELECT "Name" FROM "v_customer";
            """);

        dir.Write("Tables/Customer.sql", """
            CREATE TABLE "Customer"
            (
                "Id"   INT          NOT NULL,
                "Name" VARCHAR(100) NULL,
                CONSTRAINT "PK_Customer" PRIMARY KEY ("Id")
            );
            """);
        dir.Write("Views/v_customer.sql", """
            CREATE OR REPLACE VIEW "v_customer" AS SELECT "Id", "Name" FROM "Customer";
            """);
    }

    [PostgresFact]
    public async Task AnalyzeAsync_AlterTypeWithViewDependedOutsideSchemaDirectory_IsBlocked()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var dir = new TempSchemaDirectory();
        await ArrangeAlterTypeWithExternalDependentAsync(db, dir);

        var plan = await CreateOrchestrator().AnalyzeAsync(db.CreateSettings(dir.Path));

        Assert.True(plan.IsBlocked);
        var blocker = Assert.Single(plan.Blockers);
        Assert.Equal(ChangeType.ReplaceView, blocker.Type);
        Assert.Equal("v_customer", blocker.EntityName);
        Assert.Equal(["\"v_external\" (view)"], blocker.CascadeDropObjects);
        Assert.NotNull(blocker.BlockerMessage);
        Assert.Contains(plan.Groups, g => g.Name == PreDropGroupName);
    }

    [PostgresFact]
    public async Task AnalyzeAsync_AlterTypeWithViewDependedOutsideSchemaDirectory_AllowedIsNotBlocked()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var dir = new TempSchemaDirectory();
        await ArrangeAlterTypeWithExternalDependentAsync(db, dir);

        var plan = await CreateOrchestrator().AnalyzeAsync(db.CreateSettings(dir.Path, allowCascadeDrop: true));

        Assert.False(plan.IsBlocked);
        Assert.Contains(plan.Cautions, c => c.EntityName == "v_customer" && c.CascadeDropObjects is { Count: > 0 });
        Assert.Contains(plan.Groups, g => g.Name == PreDropGroupName);
    }

    [PostgresFact]
    public async Task ExecuteAsync_AllowedAtAnalysisButNotAtExecution_ThrowsAndLeavesDatabaseUntouched()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var dir = new TempSchemaDirectory();
        await ArrangeAlterTypeWithExternalDependentAsync(db, dir);
        var orchestrator = CreateOrchestrator();
        var plan = await orchestrator.AnalyzeAsync(db.CreateSettings(dir.Path, allowCascadeDrop: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ExecuteAsync(db.CreateSettings(dir.Path, allowCascadeDrop: false), plan));

        Assert.Equal(1L, await db.ScalarAsync<long>(
            "SELECT count(*) FROM pg_views WHERE schemaname = 'public' AND viewname = 'v_external'"));
        Assert.Equal(50, await db.ScalarAsync<int>("""
            SELECT character_maximum_length FROM information_schema.columns
            WHERE table_name = 'Customer' AND column_name = 'Name'
            """));
    }

    [PostgresFact]
    public async Task AnalyzeAsync_ViewAppendsColumnAtEnd_NotBlockedAndNoPreDrop()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var dir = new TempSchemaDirectory();
        await db.ExecuteAsync("""
            CREATE TABLE "Customer" ("Id" INT NOT NULL, "Name" VARCHAR(50) NULL, CONSTRAINT "PK_Customer" PRIMARY KEY ("Id"));
            CREATE VIEW "v_customer" AS SELECT "Id" FROM "Customer";
            CREATE VIEW "v_external" AS SELECT "Id" FROM "v_customer";
            """);
        dir.Write("Tables/Customer.sql", """
            CREATE TABLE "Customer"
            (
                "Id"   INT         NOT NULL,
                "Name" VARCHAR(50) NULL,
                CONSTRAINT "PK_Customer" PRIMARY KEY ("Id")
            );
            """);
        dir.Write("Views/v_customer.sql", """
            CREATE OR REPLACE VIEW "v_customer" AS SELECT "Id", "Name" FROM "Customer";
            """);

        var plan = await CreateOrchestrator().AnalyzeAsync(db.CreateSettings(dir.Path));

        Assert.False(plan.IsBlocked);
        Assert.DoesNotContain(plan.Groups, g => g.Name == PreDropGroupName);
        var viewChange = Assert.Single(plan.Groups.Single(g => g.Name == "Views").Changes);
        Assert.Equal(ChangeType.ExtendView, viewChange.Type);
    }

    /// <summary>
    /// 欄位連同其外鍵一起移除（並改用新欄位、新外鍵）：PG 的 DROP COLUMN 會自動刪除該欄位上的外鍵，
    /// 所以 DROP CONSTRAINT 必須排在 DROP COLUMN 之前，否則報 42704 constraint does not exist。
    /// </summary>
    [PostgresFact]
    public async Task ExecuteAsync_DropColumnTogetherWithItsForeignKey_Succeeds()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var dir = new TempSchemaDirectory();
        await db.ExecuteAsync("""
            CREATE TABLE "Service" ("Id" UUID NOT NULL, CONSTRAINT "PK_Service" PRIMARY KEY ("Id"));
            CREATE TABLE "Snapshot" (
                "Id" UUID NOT NULL,
                "ServiceId" UUID NOT NULL,
                "Tool" VARCHAR(20) NOT NULL,
                CONSTRAINT "PK_Snapshot" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_Snapshot_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "Service"("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "UIX_Snapshot_ServiceId_Tool" ON "Snapshot"("ServiceId", "Tool");
            """);

        dir.Write("Tables/Service.sql", """
            CREATE TABLE "Service" ("Id" UUID NOT NULL, CONSTRAINT "PK_Service" PRIMARY KEY ("Id"));
            """);
        dir.Write("Tables/Target.sql", """
            CREATE TABLE "Target"
            (
                "Id"        UUID NOT NULL,
                "ServiceId" UUID NOT NULL,
                CONSTRAINT "PK_Target" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_Target_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "Service"("Id") ON DELETE CASCADE
            );
            """);
        dir.Write("Tables/Snapshot.sql", """
            CREATE TABLE "Snapshot"
            (
                "Id"           UUID        NOT NULL,
                "TargetId"     UUID        NOT NULL,
                "Tool"         VARCHAR(20) NOT NULL,
                CONSTRAINT "PK_Snapshot" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_Snapshot_TargetId" FOREIGN KEY ("TargetId") REFERENCES "Target"("Id") ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX "UIX_Snapshot_TargetId_Tool" ON public."Snapshot"("TargetId", "Tool");
            """);

        var orchestrator = CreateOrchestrator();
        var settings = db.CreateSettings(dir.Path);
        var plan = await orchestrator.AnalyzeAsync(settings);
        var results = await orchestrator.ExecuteAsync(settings, plan);

        Assert.All(results, r => Assert.True(r.Success, $"{r.GroupName}: {r.ErrorMessage}"));
        Assert.Equal(0L, await db.ScalarAsync<long>("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'Snapshot' AND column_name = 'ServiceId'
            """));
        Assert.Equal(1L, await db.ScalarAsync<long>("""
            SELECT count(*) FROM pg_constraint WHERE conname = 'FK_Snapshot_TargetId'
            """));
    }
}
