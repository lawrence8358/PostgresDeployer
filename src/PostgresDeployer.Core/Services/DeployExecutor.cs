namespace PostgresDeployer.Core.Services;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using PostgresDeployer.Core.Interfaces;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Resources;

/// <summary>
/// SQL 交易執行引擎。
/// 在單一交易中執行一個 DeployGroup 的所有 SQL 語句。
/// 成功則 COMMIT，失敗則 ROLLBACK。
/// </summary>
public class DeployExecutor : IDeployExecutor
{
    private readonly string _connectionString;
    private readonly ILogger<DeployExecutor> _logger;

    public DeployExecutor(string connectionString, ILogger<DeployExecutor> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<DeployResult> ExecuteGroupAsync(
        DeployGroup group,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (group.Statements.Count == 0)
        {
            return new DeployResult
            {
                Success = true,
                GroupName = group.Name,
                StatementsExecuted = 0,
                Duration = TimeSpan.Zero
            };
        }

        var sw = Stopwatch.StartNew();
        int executed = 0;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            foreach (var sql in group.Statements)
            {
                ct.ThrowIfCancellationRequested();

                var logSql = sql.Length > 120 ? sql[..120] + "..." : sql;
                _logger.LogInformation("[SQL] {Sql}", logSql);

                progress?.Report(CoreStrings.Format("Deploy_Executing", group.Name, executed + 1, group.Statements.Count));

                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                cmd.CommandTimeout = 120;
                await cmd.ExecuteNonQueryAsync(ct);

                executed++;
            }

            await tx.CommitAsync(ct);
            sw.Stop();

            _logger.LogInformation("[OK] {Group}: {Count} statement(s) executed ({Duration:F1}s)",
                group.Name, executed, sw.Elapsed.TotalSeconds);

            return new DeployResult
            {
                Success = true,
                GroupName = group.Name,
                StatementsExecuted = executed,
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rbEx) { _logger.LogWarning(rbEx, "Rollback failed"); }
            sw.Stop();
            _logger.LogWarning("[CANCEL] {Group}: deployment cancelled by user", group.Name);

            return new DeployResult
            {
                Success = false,
                GroupName = group.Name,
                ErrorMessage = CoreStrings.Get("Deploy_Cancelled"),
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rbEx) { _logger.LogWarning(rbEx, "Rollback failed"); }
            sw.Stop();

            // 偷湋是否為 extension 導致的類別/函數不存在錯誤，給予更清楚的提示
            var errorMessage = ex.Message;
            if (ex is NpgsqlException npEx)
            {
                var sqlState = npEx.SqlState;
                // 42704 = undefined_object (type doesn't exist), 42883 = undefined_function
                if (sqlState is "42704" or "42883")
                {
                    errorMessage = ex.Message + "\n" + CoreStrings.Get("Deploy_ExtensionHint");
                }
            }

            _logger.LogError("[ERROR] {Group}: {Message}", group.Name, errorMessage);

            return new DeployResult
            {
                Success = false,
                GroupName = group.Name,
                ErrorMessage = errorMessage,
                FailedSql = executed < group.Statements.Count ? group.Statements[executed] : null,
                Duration = sw.Elapsed
            };
        }
    }
}
