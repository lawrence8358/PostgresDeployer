namespace PostgresDeployer.Core.Services;

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;
using PostgresDeployer.Core.Helpers;
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
                // 42704 = undefined_object，只有「型別不存在」才可能是缺 extension（constraint／index 不存在與 extension 無關）
                // 42883 = undefined_function
                if ((sqlState == "42704" && ex.Message.Contains("type \"", StringComparison.Ordinal))
                    || sqlState == "42883")
                {
                    errorMessage = ex.Message + "\n" + CoreStrings.Get("Deploy_ExtensionHint");
                }
            }

            var failedSql = executed < group.Statements.Count ? group.Statements[executed] : null;

            // 附上失敗的來源檔案（與行號），否則整份表檔失敗時只看得到 syntax error，難以定位
            var location = DescribeFailureLocation(group, executed, failedSql, ex);
            if (location != null)
                errorMessage = errorMessage + "\n" + location;

            _logger.LogError("[ERROR] {Group}: {Message}", group.Name, errorMessage);

            return new DeployResult
            {
                Success = false,
                GroupName = group.Name,
                ErrorMessage = errorMessage,
                FailedSql = failedSql,
                Duration = sw.Elapsed
            };
        }
    }

    /// <summary>
    /// 產生「失敗位置」說明：來源標籤（通常為檔名），若能由 PostgreSQL 錯誤位置換算則附上行號。
    /// </summary>
    private static string? DescribeFailureLocation(DeployGroup group, int statementIndex, string? failedSql, Exception ex)
    {
        if (statementIndex >= group.StatementLabels.Count)
            return null;

        var label = group.StatementLabels[statementIndex];
        var line = failedSql != null ? TryGetErrorLine(failedSql, ex) : null;

        return line.HasValue
            ? CoreStrings.Format("Deploy_FailedAtLine", label, line.Value)
            : CoreStrings.Format("Deploy_FailedAt", label);
    }

    private static readonly Regex SyntaxErrorTokenPattern = new(
        @"at or near ""(?<token>(?:[^""]|"""")*)""", RegexOptions.Compiled);

    /// <summary>
    /// 將語法錯誤（42601）的錯誤位置換算為原始 SQL 的行號。
    /// 多陳述式的 command 會被 Npgsql 拆成多段送出，Position 是相對於失敗那一段的 1-based 字元位置，
    /// 而 Npgsql 不公開是哪一段；因此逐段套用 Position，只在「near "X"」的 token 恰好對上唯一一段時才回傳行號，
    /// 無法確定時回傳 null（只顯示檔名，不給錯誤的行號）。
    /// </summary>
    private static int? TryGetErrorLine(string sql, Exception ex)
    {
        if (ex is not PostgresException { SqlState: "42601" } pgEx || pgEx.Position <= 0)
            return null;

        var tokenMatch = SyntaxErrorTokenPattern.Match(pgEx.MessageText);
        if (!tokenMatch.Success || tokenMatch.Groups["token"].Length == 0)
            return null;
        var token = tokenMatch.Groups["token"].Value.Replace("\"\"", "\"");

        int? found = null;
        foreach (var stmt in SqlTextScanner.SplitStatements(sql))
        {
            var offset = stmt.Start + pgEx.Position - 1;
            if (offset >= stmt.Start + stmt.Text.Length)
                continue;
            if (string.CompareOrdinal(sql, offset, token, 0, token.Length) != 0)
                continue;
            // token 必須完整對上（"CREAT" 不可匹配 "CREATE"）
            var after = offset + token.Length;
            if (after < sql.Length && IsWordChar(token[^1]) && IsWordChar(sql[after]))
                continue;
            if (found.HasValue)
                return null; // 多段都對得上，無法確定
            found = offset;
        }

        return found.HasValue ? SqlTextScanner.GetLineNumber(sql, found.Value) : null;

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
