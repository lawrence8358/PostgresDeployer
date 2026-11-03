namespace PostgresDeployer.Core.Models;

/// <summary>
/// 完整的部署計畫。包含所有要執行的群組和注意事項。
/// 由 DeployOrchestrator.AnalyzeAsync() 產生。
/// </summary>
public class DeployPlan
{
    /// <summary>部署群組清單（依執行順序）</summary>
    public List<DeployGroup> Groups { get; set; } = [];

    /// <summary>注意事項清單（有 CautionMessage 的變更，仍會執行但需提醒使用者）</summary>
    public List<SchemaChange> Cautions { get; set; } = [];

    /// <summary>
    /// 阻擋項目清單（有 BlockerMessage 的變更）。
    /// 只要有任何阻擋項目，整份計畫都不得執行（ExecuteAsync 會拋出例外），
    /// 除非使用者明確放行（<see cref="DeployOptions.AllowCascadeDrop"/>）。
    /// </summary>
    public List<SchemaChange> Blockers { get; set; } = [];

    /// <summary>部署是否被阻擋</summary>
    public bool IsBlocked => Blockers.Count > 0;

    /// <summary>所有群組的 SQL 語句總數</summary>
    public int TotalStatements => Groups.Sum(g => g.Statements.Count);

    /// <summary>是否有任何需要執行的變更</summary>
    public bool HasChanges => Groups.Any(g => g.Statements.Count > 0);
}
