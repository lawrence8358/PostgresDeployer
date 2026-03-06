namespace PostgresDeployer.Cli.Commands;

using Microsoft.Extensions.Logging;
using PostgresDeployer.Cli.Helpers;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public static class DeployCommand
{
    public static async Task<int> HandleAsync(
        string? config, string? host, int? port, string? database,
        string? username, string? password,
        bool dryRun, bool yes, string only, bool stopOnError,
        string? schema, string? initData,
        string? extensions, string? logFile)
    {
        try
        {
            return await HandleCoreAsync(
                config, host, port, database, username, password,
                dryRun, yes, only, stopOnError,
                schema, initData,
                extensions, logFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleCoreAsync(
        string? config, string? host, int? port, string? database,
        string? username, string? password,
        bool dryRun, bool yes, string only, bool stopOnError,
        string? schema, string? initData,
        string? extensions, string? logFile)
    {
        // 1. 合併設定
        var settings = SettingsMerger.Merge(
            config, host, port, database, username, password,
            schema, initData, extensions);

        settings.Options.StopOnError = stopOnError;

        // 2. 驗證必要參數
        if (string.IsNullOrEmpty(settings.Connection.Database))
        {
            Console.Error.WriteLine("Error: database name is required (--database or config file)");
            return 1;
        }

        // 3. 依 --only 篩選
        switch (only.ToLowerInvariant())
        {
            case "tables":
                settings.Options.ExecuteSeedData = false;
                break;
            case "seeds":
                settings.Options.ExecuteSeedData = true;
                break;
            case "views":
            case "all":
            default:
                break;
        }

        // 4. 設定日誌
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 若指定 logFile，寫入日誌到檔案
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

            // 5. 顯示連線資訊
            Console.WriteLine($"Target:   {settings.Connection.Host}:{settings.Connection.Port}/{settings.Connection.Database}");
            Console.WriteLine($"Username: {settings.Connection.Username}");
            Console.WriteLine($"Schema:   {settings.Paths.Schema}");
            Console.WriteLine();

            // 6. 測試連線
            var introspector = new SchemaIntrospector(settings.Connection.ToConnectionString());
            if (!await introspector.TestConnectionAsync())
            {
                Console.Error.WriteLine("Error: cannot connect to database");
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
            var progress = new Progress<string>(msg =>
            {
                Console.WriteLine($"  {msg}");
                logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            });
            var results = await orchestrator.ExecuteAsync(settings, plan, progress);

            // 13. 顯示結果
            Console.WriteLine();
            PrintResults(results);

            // 14. 寫入 RunScript
            try
            {
                var runScriptWriter = new RunScriptWriter();
                var hostInfo = $"{settings.Connection.Host}:{settings.Connection.Port}/{settings.Connection.Database}";
                var exeDir = AppContext.BaseDirectory;
                var scriptPath = runScriptWriter.Write(plan, hostInfo, exeDir);
                Console.WriteLine();
                Console.WriteLine($"RunScript saved: {scriptPath}");
                logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss}] RunScript saved: {scriptPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: failed to write RunScript — {ex.Message}");
            }

            return results.All(r => r.Success) ? 0 : 1;
        }
        finally
        {
            logWriter?.Dispose();
        }
    }

    private static void PrintPlanSummary(DeployPlan plan)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine(" Deployment Plan");
        Console.WriteLine("═══════════════════════════════════════════════════════");

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

        // 顯示注意事項
        if (plan.Cautions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  [Cautions] ({plan.Cautions.Count} item(s))");
            foreach (var caution in plan.Cautions)
            {
                Console.WriteLine($"    [!] {caution.CautionMessage}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  Total: {plan.TotalStatements} statement(s), {plan.Cautions.Count} caution(s)");
    }

    private static void PrintResults(List<DeployResult> results)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine(" Execution Result");
        Console.WriteLine("═══════════════════════════════════════════════════════");

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
