namespace PostgresDeployer.Cli.Helpers;

using PostgresDeployer.Core.Models;

/// <summary>
/// deploy 與 diff 指令共用的「阻擋區塊」與統計列文字。
/// 放行方式的說明已包含在每筆 <see cref="SchemaChange.BlockerMessage"/>（CoreStrings 資源），此處不再重複。
/// </summary>
public static class PlanBlockerFormatter
{
    /// <summary>
    /// 阻擋區塊：<c>[BLOCKED] (n item(s))</c> 標題，加上每筆 <c>[X] {BlockerMessage}</c>。計畫未被阻擋時回傳空清單。
    /// </summary>
    /// <param name="plan">部署計畫</param>
    /// <param name="indent">每行前綴縮排（項目列會再多縮排兩格）</param>
    public static List<string> FormatBlockedSection(DeployPlan plan, string indent = "")
    {
        if (!plan.IsBlocked)
            return [];

        var lines = new List<string>
        {
            $"{indent}[BLOCKED] ({plan.Blockers.Count} item(s)) — deployment will not run"
        };
        lines.AddRange(plan.Blockers.Select(b => $"{indent}  [X] {b.BlockerMessage}"));
        return lines;
    }

    /// <summary>
    /// 統計列：<c>Total: n statement(s), n caution(s)</c>，被阻擋時加上 <c>, n blocker(s)</c>。
    /// </summary>
    public static string FormatTotalLine(DeployPlan plan, string indent = "")
    {
        var total = $"{indent}Total: {plan.TotalStatements} statement(s), {plan.Cautions.Count} caution(s)";
        return plan.IsBlocked ? $"{total}, {plan.Blockers.Count} blocker(s)" : total;
    }
}
