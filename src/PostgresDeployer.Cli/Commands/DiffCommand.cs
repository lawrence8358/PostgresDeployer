namespace PostgresDeployer.Cli.Commands;

using Microsoft.Extensions.Logging;
using PostgresDeployer.Cli.Helpers;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public static class DiffCommand
{
    public static async Task<int> HandleAsync(CliArgs args, string? output)
    {
        try
        {
            // 1. 合併設定
            var settings = SettingsMerger.Merge(args);

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

            var logger = loggerFactory.CreateLogger<DeployOrchestrator>();

            // 4. 測試連線（CreateDatabaseIfNotExists=true 時，DB 不存在為合法狀態，
            //    Orchestrator 會自動視為空資料庫；僅需測試伺服器可達即可）
            if (settings.Options.CreateDatabaseIfNotExists)
            {
                var serverIntrospector = new SchemaIntrospector(settings.Connection.ToServerConnectionString());
                if (!await serverIntrospector.TestConnectionAsync())
                {
                    await Console.Error.WriteLineAsync("Error: cannot reach PostgreSQL server");
                    return 1;
                }
            }
            else
            {
                var introspector = new SchemaIntrospector(settings.Connection.ToConnectionString());
                if (!await introspector.TestConnectionAsync())
                {
                    await Console.Error.WriteLineAsync("Error: cannot connect to database");
                    return 1;
                }
            }

            // 6. 分析差異
            var orchestrator = new DeployOrchestrator(logger, loggerFactory);
            var plan = await orchestrator.AnalyzeAsync(settings);

            // 7. 產生報告
            var report = GenerateReport(plan);

            // 8. 輸出
            if (!string.IsNullOrEmpty(output))
            {
                var directory = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(output, report);
                await Console.Out.WriteLineAsync($"Diff report written to: {output}");
            }
            else
            {
                Console.WriteLine(report);
            }

            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string GenerateReport(DeployPlan plan)
    {
        var lines = new List<string>
        {
            "═══════════════════════════════════════════════════════",
            " Schema Diff Report",
            "═══════════════════════════════════════════════════════",
            ""
        };

        foreach (var group in plan.Groups)
        {
            if (group.Changes.Count > 0)
            {
                lines.Add($"[{group.Name}] ({group.Changes.Count} change(s))");
                foreach (var change in group.Changes)
                {
                    lines.Add($"  - {change.Description}");
                    if (change.Sql != null)
                        lines.Add($"    SQL: {change.Sql}");
                }
                lines.Add("");
            }
            else if (group.Statements.Count > 0)
            {
                lines.Add($"[{group.Name}] ({group.Statements.Count} statement(s))");
                lines.Add("");
            }
        }

        if (plan.Cautions.Count > 0)
        {
            lines.Add("[Cautions]");
            foreach (var caution in plan.Cautions)
                lines.Add($"  [!] {caution.CautionMessage}");
            lines.Add("");
        }

        lines.Add($"Total: {plan.TotalStatements} statement(s), {plan.Cautions.Count} caution(s)");

        if (!plan.HasChanges)
        {
            lines.Add("");
            lines.Add("Schema is up to date. No changes needed.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
