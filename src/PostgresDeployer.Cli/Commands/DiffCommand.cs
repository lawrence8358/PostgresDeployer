namespace PostgresDeployer.Cli.Commands;

using Microsoft.Extensions.Logging;
using PostgresDeployer.Cli.Helpers;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public static class DiffCommand
{
    public static async Task<int> HandleAsync(
        string? config, string? host, int? port, string? database,
        string? username, string? password,
        string? schema, string? initData,
        string? extensions, string? output)
    {
        try
        {
            // 1. 合併設定
            var settings = SettingsMerger.Merge(
                config, host, port, database, username, password,
                schema, initData, extensions);

            // 2. 驗證必要參數
            if (string.IsNullOrEmpty(settings.Connection.Database))
            {
                Console.Error.WriteLine("Error: database name is required (--database or config file)");
                return 1;
            }

            // 3. 設定日誌
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger<DeployOrchestrator>();

            // 4. 測試連線
            var introspector = new SchemaIntrospector(settings.Connection.ToConnectionString());
            if (!await introspector.TestConnectionAsync())
            {
                Console.Error.WriteLine("Error: cannot connect to database");
                return 1;
            }

            // 5. 分析差異
            var orchestrator = new DeployOrchestrator(logger, loggerFactory);
            var plan = await orchestrator.AnalyzeAsync(settings);

            // 6. 產生報告
            var report = GenerateReport(plan);

            // 7. 輸出
            if (!string.IsNullOrEmpty(output))
            {
                var directory = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(output, report);
                Console.WriteLine($"Diff report written to: {output}");
            }
            else
            {
                Console.WriteLine(report);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
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
                    {
                        lines.Add($"    SQL: {change.Sql}");
                    }
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
            {
                lines.Add($"  [!] {caution.CautionMessage}");
            }
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
