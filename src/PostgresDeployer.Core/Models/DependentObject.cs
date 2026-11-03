namespace PostgresDeployer.Core.Models;

/// <summary>
/// 依賴某個 View 的資料庫物件（DROP VIEW ... CASCADE 會一併刪除的 View / Materialized View）。
/// </summary>
/// <param name="Schema">所在 schema 名稱</param>
/// <param name="Name">物件名稱</param>
/// <param name="Kind">物件種類（<see cref="KindView"/> 或 <see cref="KindMaterializedView"/>）</param>
public sealed record DependentObject(string Schema, string Name, string Kind)
{
    public const string KindView = "view";
    public const string KindMaterializedView = "materialized view";

    /// <summary>
    /// 顯示用名稱：public schema 省略 schema，如 <c>"v_report"</c> (view)、<c>"rpt"."mv_sales"</c> (materialized view)。
    /// </summary>
    public override string ToString()
    {
        var name = Schema == "public" ? $"\"{Name}\"" : $"\"{Schema}\".\"{Name}\"";
        return $"{name} ({Kind})";
    }
}
