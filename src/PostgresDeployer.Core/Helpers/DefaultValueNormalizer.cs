namespace PostgresDeployer.Core.Helpers;

using System.Text.RegularExpressions;

/// <summary>
/// PostgreSQL DEFAULT 值正規化工具。
/// 將 SQL 檔案和 DB 的 Default 值正規化為相同格式，以便正確比較。
/// </summary>
public static class DefaultValueNormalizer
{
    private static readonly Regex TypeCastPattern = new(@"^(.+?)::\w[\w\s]*$", RegexOptions.Compiled);

    /// <summary>
    /// 正規化 SQL 檔案中的 DEFAULT 值。
    /// </summary>
    public static string? NormalizeFromSql(string? value)
    {
        if (value == null) return null;

        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;

        return NormalizeValue(trimmed);
    }

    /// <summary>
    /// 正規化 DB column_default 值。
    /// </summary>
    public static string? NormalizeFromDb(string? columnDefault, bool isIdentity)
    {
        if (isIdentity) return null;
        if (columnDefault == null) return null;

        var trimmed = columnDefault.Trim();
        if (trimmed.Length == 0) return null;

        // Sequence default → ignore
        if (trimmed.Contains("nextval(", StringComparison.OrdinalIgnoreCase))
            return null;

        // Strip ::type suffix (e.g., 'BabySchool'::character varying → 'BabySchool')
        var castMatch = TypeCastPattern.Match(trimmed);
        if (castMatch.Success)
            trimmed = castMatch.Groups[1].Value.Trim();

        return NormalizeValue(trimmed);
    }

    /// <summary>
    /// 比較兩個正規化後的 Default 值是否相同。
    /// </summary>
    public static bool DefaultsMatch(string? a, string? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeValue(string value)
    {
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            return "FALSE";
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            return "TRUE";
        if (string.Equals(value, "now()", StringComparison.OrdinalIgnoreCase))
            return "NOW()";

        return value;
    }
}
