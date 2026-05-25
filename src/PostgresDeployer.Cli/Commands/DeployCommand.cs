namespace PostgresDeployer.Cli.Commands;

using Microsoft.Extensions.Logging;
using PostgresDeployer.Cli.Helpers;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public static class DeployCommand
{
    private const string Separator = "═══════════════════════════════════════════════════════";

    public static async Task<int> HandleAsync(
        CliArgs args,
        bool dryRun, bool yes, string only, bool stopOnError,
        string? logFile)
    {
        try
        {
            return await HandleCoreAsync(args, dryRun, yes, only, stopOnError, logFile);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleCoreAsync(
        CliArgs args,
        bool dryRun, bool yes, string only, bool stopOnError,
        string? logFile)
    {
        // 1. 合併設定
        var settings = SettingsMerger.Merge(args);
        settings.Options.StopOnError = stopOnError;
        ApplyOnlyFilter(settings, only);

        // 2. 驗證必要參數
        if (string.IsNullOrEmpty(settings.Connection.Database))
        {
            await Console.Error.WriteLineAsync("Error: database name is required (--database, --connection-string, or config file)");
            return 1;
        }

        // 3. 設定日誌
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        StreamWriter? logWriter = null;
        if (!string.IsNullOrEmpty(logFile))
        {
            var logDir = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(logDir))
                Directory.CreateDirectory(logDir);
            logWriter = new StreamWriter(logFile, append: true) { AutoFlush = true };
        }

        try
        {
            var logger = loggerFactory.CreateLogger<DeployOrchestrator>();

            // 4. 顯示連線資訊
            Console.WriteLine($"Target:   {settings.Connection.Host}:{settings.Connection.Port}/{settings.Connection.Database}");
            Console.WriteLine($"Username: {settings.Connection.Username}");
            Console.WriteLine($"Schema:   {settings.Paths.Schema}");
            Console.WriteLine();

            // 5. 若啟用 CreateDatabaseIfNotExists，先確保資料庫存在
            if (!dryRun && settings.Options.CreateDatabaseIfNotExists)
            {
                var dbProgress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));
                await DatabaseInitializer.EnsureDatabaseExistsAsync(settings.Connection, dbProgress);
            }

            // 6. 測試連線
            var introspector = new SchemaIntrospector(settings.Connection.ToConnectionString());
            if (!await introspector.TestConnectionAsync())
            {
                await Console.Error.WriteLineAsync("Error: cannot connect to database");
                return 1;
            }
            Console.WriteLine("Database connection successful");
            Console.WriteLine();

            // 7. 分析差異
            var orchestrator = new DeployOrchestrator(logger, loggerFactory);
            var plan = await orchestrator.AnalyzeAsync(settings);

            // 8. 顯示變更摘要
            PrintPlanSummary(plan);

            // 9. Dry Run → 結束
            if (dryRun)
            {
                Console.WriteLine();
                Console.WriteLine("Dry run complete. No changes applied.");
                return 0;
            }

            // 10. 無變更 → 結束
            if (!plan.HasChanges)
            {
                Console.WriteLine("Schema is up to date. No changes needed.");
                return 0;
            }

            // 11. 確認
            if (!yes)
            {
                Console.Write("Proceed with deployment? [y/N] ");
                var input = Console.ReadLine()?.Trim().ToLower();
                if (input != "y" && input != "yes")
                {
                    Console.WriteLine("Cancelled.");
                    return 0;
                }
            }

            // 12. 執行
            var deployProgress = new Progress<string>(msg =>
            {
                Console.WriteLine($"  {msg}");
                logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            });
            var results = await orchestrator.ExecuteAsync(settings, plan, deployProgress);

            // 13. 顯示結果
            Console.WriteLine();
            PrintResults(results);

            // 14. 寫入 RunScript
            await WriteRunScriptAsync(plan, settings, logWriter);

            return results.All(r => r.Success) ? 0 : 1;
        }
        finally
        {
            if (logWriter != null)
                await logWriter.DisposeAsync();
        }
    }

    private static void ApplyOnlyFilter(DeploySettings settings, string only)
    {
        switch (only.ToLowerInvariant())
        {
            case "tables":
                settings.Options.ExecuteSeedData = false;
                break;
            case "seeds":
                settings.Options.ExecuteSeedData = true;
                break;
        }
    }

    private static async Task WriteRunScriptAsync(
        DeployPlan plan, DeploySettings settings, StreamWriter? logWriter)
    {
        try
        {
            var runScriptWriter = new RunScriptWriter();
            var hostInfo = $"{settings.Connection.Host}:{settings.Connection.Port}/{settings.Connection.Database}";
            var scriptPath = runScriptWriter.Write(plan, hostInfo, AppContext.BaseDirectory);
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync($"RunScript saved: {scriptPath}");
            logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss}] RunScript saved: {scriptPath}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Warning: failed to write RunScript — {ex.Message}");
        }
    }

    private static void PrintPlanSummary(DeployPlan plan)
    {
        Console.WriteLine(Separator);
        Console.WriteLine(" Deployment Plan");
        Console.WriteLine(Separator);

        foreach (var group in plan.Groups)
        {
            if (group.Changes.Count > 0)
            {
                Console.WriteLine($"  [{group.Name}] ({group.Changes.Count} change(s))");
                foreach (var change in group.Changes)
                {
                    var prefix = change.Type switch
                    {
                        ChangeType.CreateTable => "[+TABLE]",
                        ChangeType.AddColumn => "[+COL]  ",
                        ChangeType.AlterColumnType => "[~TYPE] ",
                        ChangeType.AlterColumnNullable => "[~NULL] ",
                        ChangeType.AlterColumnDefault => "[~DFLT] ",
                        ChangeType.CreateIndex => "[+IDX]  ",
                        ChangeType.RecreateIndex => "[~IDX]  ",
                        ChangeType.DropIndex => "[-IDX]  ",
                        ChangeType.DropColumn => "[-COL]  ",
                        ChangeType.RecreatePrimaryKey => "[~PK]   ",
                        ChangeType.CreateForeignKey => "[+FK]   ",
                        ChangeType.DropForeignKey => "[-FK]   ",
                        ChangeType.RecreateForeignKey => "[~FK]   ",
                        _ => "[?]     "
                    };
                    Console.WriteLine($"    {prefix} {change.Description}");
                }
            }
            else if (group.Statements.Count > 0)
            {
                Console.WriteLine($"  [{group.Name}] ({group.Statements.Count} statement(s))");
            }
        }

        if (plan.Cautions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  [Cautions] ({plan.Cautions.Count} item(s))");
            foreach (var caution in plan.Cautions)
                Console.WriteLine($"    [!] {caution.CautionMessage}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Total: {plan.TotalStatements} statement(s), {plan.Cautions.Count} caution(s)");
    }

    private static void PrintResults(List<DeployResult> results)
    {
        Console.WriteLine(Separator);
        Console.WriteLine(" Execution Result");
        Console.WriteLine(Separator);

        foreach (var result in results)
        {
            if (result.Success)
            {
                Console.WriteLine($"  [OK]    {result.GroupName}: {result.StatementsExecuted} statement(s) executed ({result.Duration.TotalSeconds:F1}s)");
            }
            else
            {
                Console.WriteLine($"  [FAIL]  {result.GroupName}: {result.ErrorMessage}");
                if (result.FailedSql != null)
                {
                    var failSql = result.FailedSql.Length > 100
                        ? result.FailedSql[..100] + "..."
                        : result.FailedSql;
                    Console.WriteLine($"          SQL: {failSql}");
                }
            }
        }

        var allSuccess = results.All(r => r.Success);
        Console.WriteLine();
        Console.WriteLine(allSuccess ? "  Deployment complete! All operations succeeded." : "  Deployment failed. See errors above.");
    }
}
