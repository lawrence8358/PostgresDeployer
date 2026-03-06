namespace PostgresDeployer.Core.Services;

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
        var actualTables = await introspector.GetAllTableSchemasAsync(ct);
        _logger.LogInformation("Database has {Count} table(s)", actualTables.Count);

        // ═══ Phase 3: 差異比對 ═══
        _logger.LogInformation("Computing schema differences...");
        var changes = differ.ComputeChanges(desiredTables, actualTables);

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
                    if (!tableMap.TryGetValue(change.TableName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.TableName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.TableName}: {change.ColumnName}");
                    change.Sql = generator.GenerateAddColumn(change.TableName, col);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.AlterColumnType:
                {
                    if (!tableMap.TryGetValue(change.TableName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.TableName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.TableName}: {change.ColumnName}");
                    change.Sql = generator.GenerateAlterColumnType(
                        change.TableName, change.ColumnName!, col.RawType);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.AlterColumnNullable:
                {
                    if (!tableMap.TryGetValue(change.TableName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.TableName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.TableName}: {change.ColumnName}");
                    change.Sql = generator.GenerateAlterColumnNullable(
                        change.TableName, change.ColumnName!, col.IsNullable);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.AlterColumnDefault:
                {
                    if (!tableMap.TryGetValue(change.TableName, out var table))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.TableName}");
                    var col = table.Columns.FirstOrDefault(c => c.Name == change.ColumnName)
                        ?? throw new InvalidOperationException(
                            $"Column not found in table {change.TableName}: {change.ColumnName}");
                    var defValue = col.HasDefault ? col.DefaultValue : null;
                    change.Sql = generator.GenerateAlterColumnDefault(
                        change.TableName, change.ColumnName!, defValue);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;
                }

                case ChangeType.CreateIndex:
                {
                    if (!tableMap.TryGetValue(change.TableName, out var table))
                        break;
                    var idxDef = table.Indexes.FirstOrDefault(i =>
                        change.Description.Contains(i.Name));
                    if (idxDef != null)
                    {
                        change.Sql = generator.GenerateCreateIndex(change.TableName, idxDef);
                        indexGroup.Statements.Add(change.Sql);
                        indexGroup.Changes.Add(change);
                    }
                    break;
                }

                case ChangeType.RecreateIndex:
                {
                    if (!tableMap.TryGetValue(change.TableName, out var table))
                        break;
                    var idxDef = table.Indexes.FirstOrDefault(i =>
                        change.Description.Contains(i.Name));
                    if (idxDef != null)
                    {
                        indexGroup.Statements.Add(generator.GenerateDropIndex(idxDef.Name));
                        indexGroup.Statements.Add(
                            generator.GenerateCreateIndex(change.TableName, idxDef));
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
                    change.Sql = generator.GenerateDropColumn(change.TableName, change.ColumnName!);
                    alterGroup.Statements.Add(change.Sql);
                    alterGroup.Changes.Add(change);
                    break;

                case ChangeType.RecreatePrimaryKey:
                {
                    var actualPk = actualTables.GetValueOrDefault(change.TableName)?.PrimaryKey;
                    if (!tableMap.TryGetValue(change.TableName, out var desiredTable))
                        throw new InvalidOperationException(
                            $"Diff produced a change for non-existent table: {change.TableName}");
                    var desiredPk = desiredTable.PrimaryKey;
                    if (actualPk?.ConstraintName != null)
                    {
                        alterGroup.Statements.Add(
                            generator.GenerateDropPrimaryKey(change.TableName, actualPk.ConstraintName));
                    }
                    if (desiredPk != null)
                    {
                        alterGroup.Statements.Add(
                            generator.GenerateCreatePrimaryKey(change.TableName, desiredPk));
                    }
                    alterGroup.Changes.Add(change);
                    break;
                }

                default:
                    _logger.LogWarning("Unhandled change type: {Type}, table: {Table}",
                        change.Type, change.TableName);
                    break;
            }
        }

        if (createGroup.Statements.Count > 0) plan.Groups.Add(createGroup);

        // 如果有 AlterColumnType 變更，且有 View 檔案，則先 Drop 所有 Views（避免 PG 拒絕變更被視圖依賴的欄位型別）
        bool hasAlterColumnType = changes.Any(c => c.Type == ChangeType.AlterColumnType);
        if (hasAlterColumnType && viewFiles.Count > 0)
        {
            var dropViewsGroup = new DeployGroup { Name = "Drop Views (Pre-Alter)" };
            var viewNameRegex = new System.Text.RegularExpressions.Regex(
                @"CREATE\s+(?:OR\s+REPLACE\s+)?VIEW\s+""([^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 倒序 Drop，避免依賴視圖互相鎖定（CASCADE 會自動處理依賴）
            foreach (var (fileName, content) in Enumerable.Reverse(viewFiles))
            {
                var m = viewNameRegex.Match(content);
                if (m.Success)
                {
                    var viewName = m.Groups[1].Value;
                    dropViewsGroup.Statements.Add($"DROP VIEW IF EXISTS \"{viewName}\" CASCADE;");
                    dropViewsGroup.StatementLabels.Add(viewName);
                }
            }
            if (dropViewsGroup.Statements.Count > 0)
            {
                plan.Groups.Add(dropViewsGroup);
                _logger.LogInformation("AlterColumnType detected, inserting Drop Views pre-step ({Count} view(s))",
                    dropViewsGroup.Statements.Count);
            }
        }

        if (alterGroup.Statements.Count > 0) plan.Groups.Add(alterGroup);
        if (indexGroup.Statements.Count > 0) plan.Groups.Add(indexGroup);

        // Views
        if (viewFiles.Count > 0)
        {
            var viewGroup = new DeployGroup { Name = "Views" };
            foreach (var (fileName, content) in viewFiles)
            {
                viewGroup.Statements.Add(content);
                viewGroup.StatementLabels.Add(StripFirstSegment(fileName));
            }
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
