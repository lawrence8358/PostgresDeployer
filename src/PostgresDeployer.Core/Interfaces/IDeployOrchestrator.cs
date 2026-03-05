namespace PostgresDeployer.Core.Interfaces;

using PostgresDeployer.Core.Models;

/// <summary>
/// 部署流程協調器介面。
/// 串連解析、內省、差異比對、語句產生、執行的完整流程。
/// </summary>
public interface IDeployOrchestrator
{
    /// <summary>
    /// 分析 Schema 差異，產生部署計畫（不執行任何 SQL）。
    /// 流程：解析 SQL 檔案 → 內省資料庫 → 差異比對 → 產生部署計畫
    /// </summary>
    /// <param name="settings">部署設定</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含所有群組和注意事項的部署計畫</returns>
    Task<DeployPlan> AnalyzeAsync(
        DeploySettings settings,
        CancellationToken ct = default);

    /// <summary>
    /// 執行部署計畫。依群組順序逐一執行，每個群組在獨立交易中。
    /// </summary>
    /// <param name="settings">部署設定</param>
    /// <param name="plan">由 AnalyzeAsync 產生的部署計畫</param>
    /// <param name="progress">進度回報（可選，供 UI 使用）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>各群組的執行結果清單</returns>
    Task<List<DeployResult>> ExecuteAsync(
        DeploySettings settings,
        DeployPlan plan,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
