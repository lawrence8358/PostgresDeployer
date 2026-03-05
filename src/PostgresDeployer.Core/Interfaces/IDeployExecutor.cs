namespace PostgresDeployer.Core.Interfaces;

using PostgresDeployer.Core.Models;

/// <summary>
/// 部署執行器介面。
/// 負責在交易中執行 SQL 語句群組。
/// </summary>
public interface IDeployExecutor
{
    /// <summary>
    /// 在單一交易中執行一個部署群組的所有 SQL 語句。
    /// 成功則 COMMIT，失敗則 ROLLBACK。
    /// </summary>
    /// <param name="group">要執行的部署群組</param>
    /// <param name="progress">進度回報（可選，供 UI 使用）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>執行結果</returns>
    Task<DeployResult> ExecuteGroupAsync(
        DeployGroup group,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
