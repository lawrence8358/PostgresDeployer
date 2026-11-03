namespace PostgresDeployer.Core.Helpers;

using PostgresDeployer.Core.Models;

/// <summary>
/// 索引定義比對工具，由 SchemaDiffer（差異比對）與 DeployOrchestrator（決定是否需要先請 PostgreSQL 正規化）共用。
/// </summary>
public static class IndexDefinitionComparer
{
    /// <summary>
    /// 比對兩個索引定義是否相同（唯一性、存取方法、欄位與排序、INCLUDE 欄位、WHERE 條件）。
    /// 運算式欄位與 WHERE 條件以 <see cref="IndexExpressionNormalizer.FragmentsEqual"/> 正規化後比較。
    /// </summary>
    public static bool Matches(IndexDefinition a, IndexDefinition b)
    {
        if (a.IsUnique != b.IsUnique) return false;
        if (!string.Equals(NormalizeMethod(a.Method), NormalizeMethod(b.Method), StringComparison.Ordinal)) return false;
        if (a.Columns.Count != b.Columns.Count) return false;
        for (int i = 0; i < a.Columns.Count; i++)
        {
            if (!IndexItemsMatch(a.Columns[i].Name, a.Columns[i].IsExpression, b.Columns[i].Name, b.Columns[i].IsExpression))
                return false;
            if (a.Columns[i].IsDescending != b.Columns[i].IsDescending) return false;
        }
        if (a.IncludeColumns.Count != b.IncludeColumns.Count) return false;
        for (int i = 0; i < a.IncludeColumns.Count; i++)
        {
            if (!string.Equals(a.IncludeColumns[i], b.IncludeColumns[i], StringComparison.Ordinal)) return false;
        }
        return IndexExpressionNormalizer.FragmentsEqual(a.Predicate, b.Predicate);

        static string NormalizeMethod(string? m) =>
            string.IsNullOrEmpty(m) ? "btree" : m.ToLowerInvariant();

        static bool IndexItemsMatch(string a, bool aExpr, string b, bool bExpr) =>
            !aExpr && !bExpr
                ? string.Equals(a, b, StringComparison.Ordinal)
                : IndexExpressionNormalizer.FragmentsEqual(a, b);
    }
}
