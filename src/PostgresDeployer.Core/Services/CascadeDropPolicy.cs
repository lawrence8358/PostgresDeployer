namespace PostgresDeployer.Core.Services;

using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Resources;

/// <summary>
/// 「Pre-Drop 的 <c>DROP VIEW ... CASCADE</c> 會刪除 schema 目錄外物件」的阻擋判斷（單一來源）。
/// 分析（<see cref="DeployOrchestrator.ApplyCascadeDropPolicy"/>）與執行（<see cref="DeployOrchestrator.ExecuteAsync"/>）共用，
/// 執行時以「當下的設定」重新判斷，避免分析後才取消放行仍執行 CASCADE。
/// </summary>
public static class CascadeDropPolicy
{
    /// <summary>
    /// 收集計畫中 <see cref="SchemaChange.CascadeDropObjects"/> 非空的變更（來源：Blockers、Cautions、各群組 Changes，去除重複）。
    /// </summary>
    public static List<SchemaChange> FindCascadeDropChanges(DeployPlan plan) =>
        plan.Blockers
            .Concat(plan.Cautions)
            .Concat(plan.Groups.SelectMany(g => g.Changes))
            .Where(c => c.CascadeDropObjects is { Count: > 0 })
            .Distinct()
            .ToList();

    /// <summary>
    /// 依 <paramref name="options"/> 判斷會阻擋部署的變更：未放行（<see cref="DeployOptions.AllowCascadeDrop"/> = false）時
    /// 為所有 CASCADE 會刪除 schema 目錄外物件的變更；放行時為空清單。
    /// </summary>
    public static List<SchemaChange> GetBlockingChanges(DeployPlan plan, DeployOptions options) =>
        options.AllowCascadeDrop ? [] : FindCascadeDropChanges(plan);

    /// <summary>
    /// 阻擋訊息：說明會被刪除的物件與放行方式。
    /// </summary>
    public static string BuildBlockerMessage(SchemaChange change) =>
        CoreStrings.Format(
            "Diff_ReplaceView_CascadeBlocked",
            change.EntityName, string.Join(", ", change.CascadeDropObjects ?? []));
}
