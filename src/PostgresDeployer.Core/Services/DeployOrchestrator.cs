namespace PostgresDeployer.Core.Services;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PostgresDeployer.Core.Interfaces;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Resources;

/// <summary>
/// 部署流程協調器。
/// 串連 Parse → Introspect → Diff → Generate → Execute 的完整部署流程。
/// </summary>
public class DeployOrchestrator : IDeployOrchestrator
{
    private readonly ILogger<DeployOrchestrator> _logger;
    private readonly ILoggerFactory? _loggerFactory;

    public DeployOrchestrator(ILogger<DeployOrchestrator> logger, ILoggerFactory? loggerFactory = null)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<DeployPlan> AnalyzeAsync(DeploySettings settings, CancellationToken ct = default)
    {
        var plan = new DeployPlan();
        var parser = new SqlFileParser();
        var introspector = new SchemaIntrospector(settings.Connection.ToConnectionString());
        var differ = new SchemaDiffer();
        var generator = new SqlGenerator();
        var paths = settings.Paths;

        // ═══ Phase 1: 解析 SQL 定義檔 ═══
        _logger.LogInformation("Parsing SQL definition files...");

        // Extensions
        var extGroup = new DeployGroup { Name = "Extensions" };
        foreach (var ext in settings.Extensions)
        {
            extGroup.Statements.Add($"CREATE EXTENSION IF NOT EXISTS \"{ext}\";");
            extGroup.StatementLabels.Add(ext);
        }
        if (extGroup.Statements.Count > 0)
            plan.Groups.Add(extGroup);

        // Schema 資料夾：讀取所有 *.sql（遞迴），依 SQL 內容自動分類
        var schemaFiles = parser.ReadSqlFiles(paths.Schema);
        _logger.LogInformation("Found {Count} SQL file(s) in schema directory", schemaFiles.Count);

        var sequenceFiles  = new List<(string FileName, string Content)>();
        var functionFiles  = new List<(string FileName, string Content)>();
        var procedureFiles = new List<(string FileName, string Content)>();
        var desiredTables  = new List<TableSchema>();
        var viewFiles      = new List<(string FileName, string Content)>();

        foreach (var (fileName, content) in schemaFiles)
        {
            ct.ThrowIfCancellationRequested();
            var fileType = SqlSchemaDetector.Detect(content);
            switch (fileType)
            {
                case Models.SchemaFileType.Sequence:
                    sequenceFiles.Add((fileName, content));
                    break;
                case Models.SchemaFileType.Function:
                    functionFiles.Add((fileName, content));
                    break;
                case Models.SchemaFileType.Procedure:
                    procedureFiles.Add((fileName, content));
                    break;
                case Models.SchemaFileType.Table:
                    TableSchema table;
                    try
                    {
                        table = parser.ParseTableSql(content);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // 附上檔名，方便定位是哪個 SQL 檔有問題
                        throw new InvalidOperationException($"{fileName}: {ex.Message}", ex);
                    }
                    table.SourceFile = fileName;
                    desiredTables.Add(table);
                    break;
                case Models.SchemaFileType.View:
                    viewFiles.Add((fileName, content));
                    break;
                default:
                    _logger.LogWarning("Unrecognized SQL file type, skipping: {FileName}", fileName);
                    break;
            }
        }

        _logger.LogInformation(
            "Classification — Sequences: {Seq}, Functions: {Func}, Procedures: {Proc}, Tables: {Tbl}, Views: {View}",
            sequenceFiles.Count, functionFiles.Count, procedureFiles.Count,
            desiredTables.Count, viewFiles.Count);

        // 按外鍵依賴拓撲排序，確保被引用的資料表先建立
        desiredTables = TopologicalSortTables(desiredTables);

        // Sequences
        if (sequenceFiles.Count > 0)
        {
            var seqGroup = new DeployGroup { Name = "Sequences" };
            foreach (var (fileName, content) in sequenceFiles)
            {
                seqGroup.Statements.Add(content);
                seqGroup.StatementLabels.Add(StripFirstSegment(fileName));
            }
            plan.Groups.Add(seqGroup);
        }

        // Functions
        if (functionFiles.Count > 0)
        {
            var funcGroup = new DeployGroup { Name = "Functions" };
            foreach (var (fileName, content) in functionFiles)
            {
                funcGroup.Statements.Add(content);
                funcGroup.StatementLabels.Add(StripFirstSegment(fileName));
            }
            plan.Groups.Add(funcGroup);
        }

        // Procedures
        if (procedureFiles.Count > 0)
        {
            var procGroup = new DeployGroup { Name = "Procedures" };
            foreach (var (fileName, content) in procedureFiles)
            {
                procGroup.Statements.Add(content);
                procGroup.StatementLabels.Add(StripFirstSegment(fileName));
            }
            plan.Groups.Add(procGroup);
        }

        // ═══ Phase 2: 內省資料庫 ═══
        _logger.LogInformation("Introspecting database schema...");
        Dictionary<string, TableSchema> actualTables;
        Dictionary<string, List<string>> existingViewColumns;

        if (settings.Options.CreateDatabaseIfNotExists)
        {
            // 若啟用自動建立，先檢查 DB 是否存在；不存在則視為空資料庫（所有物件皆為新增），
            // 此階段不建立 DB（由呼叫端的 deploy 流程負責建立）。
            var serverConnStr = settings.Connection.ToServerConnectionString();
            var dbExists = await DatabaseInitializer.DatabaseExistsAsync(serverConnStr, settings.Connection.Database, ct);
            if (dbExists)
            {
                actualTables = await introspector.GetAllTableSchemasAsync(ct);
                existingViewColumns = await introspector.GetViewColumnNamesAsync(ct);
            }
            else
            {
                _logger.LogInformation(
                    "Database '{Database}' does not exist — treating as empty (all schema objects will be shown as new)",
                    settings.Connection.Database);
                actualTables = new Dictionary<string, TableSchema>();
                existingViewColumns = [];
            }
        }
        else
        {
            actualTables = await introspector.GetAllTableSchemasAsync(ct);
            existingViewColumns = await introspector.GetViewColumnNamesAsync(ct);
        }

        _logger.LogInformation("Database has {Count} table(s), {ViewCount} view(s)",
            actualTables.Count, existingViewColumns.Count);

        // ═══ Phase 3: 差異比對 ═══
        await CanonicalizeIndexesAsync(introspector, desiredTables, actualTables, ct);

        _logger.LogInformation("Computing schema differences...");
        var changes = differ.ComputeChanges(desiredTables, actualTables);

        // View 差異比對：從 SQL 檔案中提取 View 名稱，與 DB 現有 View 比對
        var desiredViews = new List<(string ViewName, string SqlContent)>();
        foreach (var (fileName, content) in viewFiles)
        {
            var m = ViewNameRegex.Match(content);
            if (m.Success)
                desiredViews.Add((m.Groups[1].Value, content));
            else
                _logger.LogWarning("Cannot extract view name from file '{FileName}', skipping diff analysis for this view", fileName);
        }
        var viewChanges = differ.ComputeViewChanges(desiredViews, existingViewColumns);

        // ═══ Phase 4: 分類變更到對應群組 ═══
        var duplicates = desiredTables
            .GroupBy(t => t.TableName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                $"SQL file conflict: multiple files define the same table name: {string.Join(", ", duplicates)}. Ensure each SQL file's table name matches its filename.");

        var tableMap = desiredTables.ToDictionary(t => t.TableName);
        var createGroup = new DeployGroup { Name = DeployGroupNames.NewTables };
        var alterGroup  = new DeployGroup { Name = DeployGroupNames.TableChanges };
        var indexGroup  = new DeployGroup { Name = DeployGroupNames.Indexes };

        foreach (var change in changes)
        {
            ct.ThrowIfCancellationRequested();

            if (change.CautionMessage != null)
                plan.Cautions.Add(change);

            switch (change.Type)
            {
                case ChangeType.CreateTable:
                    createGroup.Statements.Add(change.Sql!);
                    createGroup.StatementLabels.Add(
                        StripFirstSegment(tableMap[change.EntityName].SourceFile ?? change.EntityName));
                    createGroup.Changes.Add(change);
                    break;

                case ChangeType.AddColumn:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.EntityName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.EntityName}: {change.ColumnName}");
                    change.Sql = generator.GenerateAddColumn(change.EntityName, col);
                    alterGroup.Statements.Add(change.Sql);
                    if (col.Comment != null)
                    {
                        alterGroup.Statements.Add(
                            generator.GenerateAlterColumnComment(change.EntityName, change.ColumnName!, col.Comment));
                    }
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.AlterColumnType:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.EntityName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.EntityName}: {change.ColumnName}");
                    change.Sql = generator.GenerateAlterColumnType(
                        change.EntityName, change.ColumnName!, col.RawType);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.AlterColumnNullable:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.EntityName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.EntityName}: {change.ColumnName}");
                    change.Sql = generator.GenerateAlterColumnNullable(
                        change.EntityName, change.ColumnName!, col.IsNullable);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.AlterColumnDefault:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.EntityName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.EntityName}: {change.ColumnName}");
                    var defValue = col.HasDefault ? col.DefaultValue : null;
                    change.Sql = generator.GenerateAlterColumnDefault(
                        change.EntityName, change.ColumnName!, defValue);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.AlterTableComment:
                    change.Sql = generator.GenerateAlterTableComment(
                        change.EntityName,
                        tableMap[change.EntityName].Comment);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;

                case ChangeType.AlterColumnComment:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.EntityName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.EntityName}: {change.ColumnName}");
                    change.Sql = generator.GenerateAlterColumnComment(
                        change.EntityName, change.ColumnName!, col.Comment);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.CreateIndex:
                {
                    var idxDef = FindIndex(tableMap, change);
                    change.Sql = generator.GenerateCreateIndex(change.EntityName, idxDef);
                    indexGroup.Statements.Add(change.Sql);
                    indexGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.RecreateIndex:
                {
                    var idxDef = FindIndex(tableMap, change);
                    indexGroup.Statements.Add(generator.GenerateDropIndex(idxDef.Name));
                    change.Sql = generator.GenerateCreateIndex(change.EntityName, idxDef);
                    indexGroup.Statements.Add(change.Sql);
                    indexGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.DropIndex:
                {
                    var dropIdx = FindIndex(actualTables, change);
                    change.Sql = generator.GenerateDropIndex(dropIdx.Name);
                    indexGroup.Statements.Add(change.Sql);
                    indexGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.DropColumn:
                    change.Sql = generator.GenerateDropColumn(change.EntityName, change.ColumnName!);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;

                case ChangeType.RecreatePrimaryKey:
                {
                    var actualPk = actualTables.GetValueOrDefault(change.EntityName)?.PrimaryKey;
                    if (!tableMap.TryGetValue(change.EntityName, out var desiredTable))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.EntityName}");
                    var desiredPk = desiredTable.PrimaryKey;
                    if (actualPk?.ConstraintName != null)
                    {
                        alterGroup.Statements.Add(
                            generator.GenerateDropPrimaryKey(change.EntityName, actualPk.ConstraintName));
                    }
                    if (desiredPk != null)
                    {
                        alterGroup.Statements.Add(
                            generator.GenerateCreatePrimaryKey(change.EntityName, desiredPk));
                    }
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.CreateForeignKey:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        break;
                    var fk = table.ForeignKeys.FirstOrDefault(f => f.ConstraintName == change.ColumnName);
                    if (fk != null)
                    {
                        change.Sql = generator.GenerateAddForeignKey(change.EntityName, fk);
                        alterGroup.Statements.Add(change.Sql);
                        alterGroup.Changes.Add(change);
                    }
                    break;
                }

                case ChangeType.DropForeignKey:
                    change.Sql = generator.GenerateDropForeignKey(change.EntityName, change.ColumnName!);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;

                case ChangeType.RecreateForeignKey:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        break;
                    var fk = table.ForeignKeys.FirstOrDefault(f => f.ConstraintName == change.ColumnName);
                    if (fk != null)
                    {
                        alterGroup.Statements.Add(
                            generator.GenerateDropForeignKey(change.EntityName, change.ColumnName!));
                        var addSql = generator.GenerateAddForeignKey(change.EntityName, fk);
                        alterGroup.Statements.Add(addSql);
                        change.Sql = addSql;
                        alterGroup.Changes.Add(change);
                    }
                    break;
                }

                default:
                    _logger.LogWarning("Unhandled change type: {Type}, table: {Table}",
                        change.Type, change.EntityName);
                    break;
            }
        }

        if (createGroup.Statements.Count > 0) plan.Groups.Add(createGroup);

        // 對於需要取代的 View（ReplaceView），在 Table 變更執行前先 DROP，
        // 避免 PG 拒絕：(1) 欄位型別變更時的依賴鎖定，(2) CREATE OR REPLACE 無法改變欄位定義（42P16）
        var replaceViewChanges = viewChanges.Where(c => c.Type == ChangeType.ReplaceView).ToList();
        if (replaceViewChanges.Count > 0)
        {
            await AddCascadeDropCautionsAsync(
                introspector, replaceViewChanges, desiredViews.Select(v => v.ViewName), ct);
            plan.Cautions.AddRange(replaceViewChanges.Where(c => c.CautionMessage != null));

            var dropViewsGroup = new DeployGroup { Name = "Drop Views (Pre-Alter)" };

            // 倒序 Drop，減少視圖間相依性衝突（CASCADE 會自動處理剩餘依賴）
            foreach (var vc in Enumerable.Reverse(replaceViewChanges))
            {
                dropViewsGroup.Statements.Add($"DROP VIEW IF EXISTS \"{vc.EntityName}\" CASCADE;");
                dropViewsGroup.StatementLabels.Add(vc.EntityName);
            }
            plan.Groups.Add(dropViewsGroup);
            _logger.LogInformation("Inserting Drop Views pre-step for {Count} existing view(s)",
                replaceViewChanges.Count);
        }

        if (alterGroup.Statements.Count > 0) plan.Groups.Add(alterGroup);
        if (indexGroup.Statements.Count > 0) plan.Groups.Add(indexGroup);

        // Views：所有 viewFiles 皆以 CREATE OR REPLACE VIEW 部署（idempotent）。
        // viewChanges 僅用於 UI 顯示（Diff 結果）與決定 Pre-Drop 對象；
        // 欄位未變但 SQL 主體有修改的 View 仍會被正確更新。
        if (viewFiles.Count > 0)
        {
            var viewGroup = new DeployGroup { Name = "Views" };
            foreach (var (fileName, content) in viewFiles)
            {
                viewGroup.Statements.Add(content);
                viewGroup.StatementLabels.Add(StripFirstSegment(fileName));
            }
            viewGroup.Changes.AddRange(viewChanges); // Diff 結果供 UI 顯示
            plan.Groups.Add(viewGroup);
        }

        // Init Data
        if (settings.Options.ExecuteSeedData)
        {
            var seedFiles = parser.ReadSqlFiles(paths.InitData);
            if (seedFiles.Count > 0)
            {
                var seedGroup = new DeployGroup { Name = "Init Data" };
                foreach (var (fileName, content) in seedFiles)
                {
                    seedGroup.Statements.Add(content);
                    seedGroup.StatementLabels.Add(fileName);
                }
                plan.Groups.Add(seedGroup);
            }
        }

        // CASCADE 會刪除 schema 目錄外物件的變更：依設定分流為阻擋項目或注意事項
        ApplyCascadeDropPolicy(plan, settings.Options, _logger);

        _logger.LogInformation(
            "Analysis complete: {Groups} group(s), {Statements} statement(s), {Cautions} caution(s), {Blockers} blocker(s)",
            plan.Groups.Count, plan.TotalStatements, plan.Cautions.Count, plan.Blockers.Count);

        return plan;
    }

    public async Task<List<DeployResult>> ExecuteAsync(
        DeploySettings settings,
        DeployPlan plan,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // 被阻擋的計畫一律拒絕執行（在連線前檢查，不執行任何群組），保護所有呼叫端
        EnsureNotBlocked(settings, plan);

        var connStr = settings.Connection.ToConnectionString();

        var executorLogger = _loggerFactory?.CreateLogger<DeployExecutor>()
            ?? NullLogger<DeployExecutor>.Instance;

        var executor = new DeployExecutor(connStr, executorLogger);
        var results = new List<DeployResult>();

        int groupIndex = 0;
        int totalGroups = plan.Groups.Count(g => g.Statements.Count > 0);

        foreach (var group in plan.Groups)
        {
            if (group.Statements.Count == 0) continue;

            groupIndex++;
            progress?.Report(CoreStrings.Format("Deploy_Progress", groupIndex, totalGroups, group.Name));

            var result = await executor.ExecuteGroupAsync(group, progress, ct);
            results.Add(result);

            if (!result.Success && settings.Options.StopOnError)
            {
                _logger.LogError("Group \"{Group}\" failed, stopping deployment: {Error}",
                    group.Name, result.ErrorMessage);
                break;
            }
        }

        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);
        _logger.LogInformation("Deployment complete: {Success} succeeded, {Fail} failed",
            successCount, failCount);

        return results;
    }

    /// <summary>
    /// 計畫被阻擋且未放行（<see cref="DeployOptions.AllowCascadeDrop"/> = false）時拋出 <see cref="InvalidOperationException"/>。
    /// </summary>
    private void EnsureNotBlocked(DeploySettings settings, DeployPlan plan)
    {
        if (!plan.IsBlocked)
            return;

        if (settings.Options.AllowCascadeDrop)
        {
            _logger.LogWarning(
                "Deploy plan has {Count} blocker(s) but AllowCascadeDrop is enabled — proceeding", plan.Blockers.Count);
            return;
        }

        var details = string.Join(Environment.NewLine, plan.Blockers.Select(b => $"  - {b.BlockerMessage}"));
        _logger.LogError("Deployment blocked, no group executed: {Count} blocker(s)", plan.Blockers.Count);
        throw new InvalidOperationException(
            CoreStrings.Format("Deploy_Blocked", plan.Blockers.Count) + Environment.NewLine + details);
    }

    /// <summary>
    /// 依 SchemaChange 的索引名稱（ColumnName）精確取得索引定義。
    /// 不可用 Description 子字串比對：名稱互為子字串時（IX_X ⊂ UIX_X_...）會取到錯誤的索引。
    /// </summary>
    private static IndexDefinition FindIndex(IReadOnlyDictionary<string, TableSchema> tables, SchemaChange change)
    {
        if (!tables.TryGetValue(change.EntityName, out var table))
            throw new InvalidOperationException(
                $"Diff produced a change for non-existent table: {change.EntityName}");
        return table.Indexes.FirstOrDefault(i => string.Equals(i.Name, change.ColumnName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Index not found in table {change.EntityName}: {change.ColumnName}");
    }

    /// <summary>
    /// SQL 檔與資料庫寫法不同的同名索引（WHERE 條件、運算式欄位），先請 PostgreSQL 正規化 SQL 檔中的定義再比對，
    /// 避免每次部署都誤判為「定義已變更」而重建索引。
    /// </summary>
    private async Task CanonicalizeIndexesAsync(
        SchemaIntrospector introspector,
        List<TableSchema> desiredTables,
        Dictionary<string, TableSchema> actualTables,
        CancellationToken ct)
    {
        var candidates = new List<(string TableName, IndexDefinition Index)>();
        foreach (var table in desiredTables)
        {
            if (!actualTables.TryGetValue(table.TableName, out var actualTable))
                continue;

            foreach (var idx in table.Indexes)
            {
                var actualIdx = actualTable.Indexes.FirstOrDefault(
                    i => string.Equals(i.Name, idx.Name, StringComparison.Ordinal));
                if (actualIdx == null || SchemaDiffer.IndexesMatch(idx, actualIdx))
                    continue;
                if (idx.Predicate != null || actualIdx.Predicate != null || idx.Columns.Any(c => c.IsExpression))
                    candidates.Add((table.TableName, idx));
            }
        }

        if (candidates.Count == 0)
            return;

        try
        {
            await introspector.CanonicalizeIndexesAsync(candidates, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to canonicalize index definitions, comparing SQL text as written");
        }
    }

    /// <summary>
    /// Pre-Alter 的 DROP VIEW ... CASCADE 會連帶刪除依賴該 View 的物件；
    /// 其中不在 schema 目錄中的物件部署後不會重建。
    /// 此方法只負責「偵測」：把這些物件記在 <see cref="SchemaChange.CascadeDropObjects"/> 並填入 CautionMessage；
    /// 要列為阻擋項目或注意事項，由 AnalyzeAsync 結尾的 <see cref="ApplyCascadeDropPolicy"/> 依設定統一分流。
    /// </summary>
    private async Task AddCascadeDropCautionsAsync(
        SchemaIntrospector introspector,
        List<SchemaChange> replaceViewChanges,
        IEnumerable<string> desiredViewNames,
        CancellationToken ct)
    {
        var desired = desiredViewNames
            .Select(v => $"\"{v}\" (view)")
            .ToHashSet(StringComparer.Ordinal);

        var dependents = await introspector.GetViewDependentsAsync(
            replaceViewChanges.Select(c => c.EntityName).ToList(), ct);

        foreach (var change in replaceViewChanges)
        {
            if (!dependents.TryGetValue(change.EntityName, out var list))
                continue;

            var lost = list.Where(d => !desired.Contains(d)).ToList();
            if (lost.Count == 0)
                continue;

            change.CascadeDropObjects = lost;
            change.CautionMessage = CoreStrings.Format(
                "Diff_ReplaceView_CascadeCaution", change.EntityName, string.Join(", ", lost));
            _logger.LogWarning(
                "DROP VIEW \"{View}\" CASCADE will drop object(s) not in the schema directory: {Objects}",
                change.EntityName, string.Join(", ", lost));
        }
    }

    /// <summary>
    /// 依 <see cref="DeployOptions.AllowCascadeDrop"/> 分流「CASCADE 會刪除 schema 目錄外物件」的變更：
    /// 未放行 → 自 Cautions 移到 <see cref="DeployPlan.Blockers"/>（整份計畫不得執行）；放行 → 維持 Caution。
    /// 從 plan 的 Cautions 與各群組 Changes 收集，不依賴 Pre-Drop 清單的產生方式。
    /// </summary>
    internal static void ApplyCascadeDropPolicy(DeployPlan plan, DeployOptions options, ILogger? logger = null)
    {
        var cascading = plan.Cautions
            .Concat(plan.Groups.SelectMany(g => g.Changes))
            .Where(c => c.CascadeDropObjects is { Count: > 0 })
            .Distinct()
            .ToList();

        if (cascading.Count == 0)
            return;

        if (options.AllowCascadeDrop)
        {
            foreach (var change in cascading)
            {
                if (!plan.Cautions.Contains(change))
                    plan.Cautions.Add(change);
            }
            logger?.LogWarning(
                "AllowCascadeDrop is enabled: {Count} DROP VIEW ... CASCADE operation(s) will drop object(s) outside the schema directory",
                cascading.Count);
            return;
        }

        foreach (var change in cascading)
        {
            change.BlockerMessage = CoreStrings.Format(
                "Diff_ReplaceView_CascadeBlocked",
                change.EntityName, string.Join(", ", change.CascadeDropObjects!));
            plan.Cautions.Remove(change);
            if (!plan.Blockers.Contains(change))
                plan.Blockers.Add(change);
        }
        logger?.LogError(
            "Deployment blocked: {Count} DROP VIEW ... CASCADE operation(s) would drop object(s) outside the schema directory",
            cascading.Count);
    }

    private static readonly Regex FkReferencesPattern = new(
        @"\bREFERENCES\s+""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ViewNameRegex = new(
        @"CREATE\s+(?:OR\s+REPLACE\s+)?VIEW\s+(?:(?:""[^""]*""|\w+)\s*\.\s*)?""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 依外鍵依賴進行拓撲排序（Kahn 演算法），確保被引用的資料表先出現。
    /// 若存在迴圈依賴，剩餘資料表以原始順序附加於末尾。
    /// </summary>
    private static List<TableSchema> TopologicalSortTables(List<TableSchema> tables)
    {
        if (tables.Count <= 1)
            return tables;

        var tableNames = tables.Select(t => t.TableName).ToHashSet(StringComparer.Ordinal);
        var tableMap   = tables.ToDictionary(t => t.TableName);

        // deps[table] = 此資料表所依賴的外部資料表名稱集合
        var deps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var refs = FkReferencesPattern.Matches(table.RawSql ?? "")
                .Select(m => m.Groups[1].Value)
                .Where(name => tableNames.Contains(name) && name != table.TableName)
                .ToHashSet(StringComparer.Ordinal);
            deps[table.TableName] = refs;
        }

        // successors[A] = 依賴 A 的資料表集合（A 必須先建立）
        var successors = tables.ToDictionary(
            t => t.TableName,
            _ => new HashSet<string>(StringComparer.Ordinal));
        var inDegree = tables.ToDictionary(t => t.TableName, _ => 0);

        foreach (var (table, tableRefs) in deps)
        {
            foreach (var dep in tableRefs)
            {
                successors[dep].Add(table);
                inDegree[table]++;
            }
        }

        // Kahn 演算法：由 in-degree=0 的節點開始
        var queue = new Queue<string>(
            tables
                .Where(t => inDegree[t.TableName] == 0)
                .Select(t => t.TableName)
                .OrderBy(n => n, StringComparer.Ordinal));

        var result = new List<TableSchema>(tables.Count);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(tableMap[current]);

            foreach (var next in successors[current].OrderBy(n => n, StringComparer.Ordinal))
            {
                if (--inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        // 若有迴圈依賴，將剩餘資料表以原始順序附加
        if (result.Count < tables.Count)
        {
            var processed = result.Select(t => t.TableName).ToHashSet(StringComparer.Ordinal);
            foreach (var table in tables)
                if (!processed.Contains(table.TableName))
                    result.Add(table);
        }

        return result;
    }
    /// <summary>
    /// 移除相對路徑的第一個目錄區段（通常為與群組名稱重複的類型資料夾），保留後續深層路徑。
    /// 例：Sequences\SequenceNo.sql → SequenceNo.sql
    ///     Sequences\v2\SequenceNo.sql → v2\SequenceNo.sql
    ///     SequenceNo.sql（無子目錄）→ SequenceNo.sql（維持不變）
    /// </summary>
    private static string StripFirstSegment(string relativeFilePath)
    {
        var idx = relativeFilePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return idx >= 0 ? relativeFilePath[(idx + 1)..] : relativeFilePath;
    }
}
