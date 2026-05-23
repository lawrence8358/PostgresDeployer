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
                    desiredTables.Add(parser.ParseTableSql(content));
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

                case ChangeType.CreateIndex:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        break;
                    var idxDef = table.Indexes.FirstOrDefault(i =>
                        change.Description.Contains(i.Name));
                    if (idxDef != null)
                    {
                        change.Sql = generator.GenerateCreateIndex(change.EntityName, idxDef);
                        indexGroup.Statements.Add(change.Sql);
                        indexGroup.Changes.Add(change);
                    }
                    break;
                }

                case ChangeType.RecreateIndex:
                {
                    if (!tableMap.TryGetValue(change.EntityName, out var table))
                        break;
                    var idxDef = table.Indexes.FirstOrDefault(i =>
                        change.Description.Contains(i.Name));
                    if (idxDef != null)
                    {
                        indexGroup.Statements.Add(generator.GenerateDropIndex(idxDef.Name));
                        indexGroup.Statements.Add(
                            generator.GenerateCreateIndex(change.EntityName, idxDef));
                        indexGroup.Changes.Add(change);
                    }
                    break;
                }

                case ChangeType.DropIndex:
                {
                    var dropIdxName = actualTables.Values
                        .SelectMany(t => t.Indexes)
                        .FirstOrDefault(i => change.Description.Contains(i.Name))?.Name;
                    if (dropIdxName != null)
                    {
                        change.Sql = generator.GenerateDropIndex(dropIdxName);
                        indexGroup.Statements.Add(change.Sql);
                        indexGroup.Changes.Add(change);
                    }
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

        _logger.LogInformation("Analysis complete: {Groups} group(s), {Statements} statement(s), {Cautions} caution(s)",
            plan.Groups.Count, plan.TotalStatements, plan.Cautions.Count);

        return plan;
    }

    public async Task<List<DeployResult>> ExecuteAsync(
        DeploySettings settings,
        DeployPlan plan,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
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

    private static readonly Regex FkReferencesPattern = new(
        @"\bREFERENCES\s+""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ViewNameRegex = new(
        @"CREATE\s+(?:OR\s+REPLACE\s+)?VIEW\s+(?:""[^""]*""\s*\.\s*)?""([^""]+)""",
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
