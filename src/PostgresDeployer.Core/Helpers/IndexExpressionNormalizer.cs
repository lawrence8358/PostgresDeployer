namespace PostgresDeployer.Core.Helpers;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 索引欄位／運算式／WHERE 條件的正規化與比較工具。
/// SQL 定義檔與 pg_catalog 對同一個索引的寫法可能不同（大小寫、空白、外層括號），
/// 比對前需先正規化。
/// </summary>
public static class IndexExpressionNormalizer
{
    private static readonly Regex QuotedIdentifierPattern = new(
        @"^""((?:[^""]|"""")+)""$", RegexOptions.Compiled);

    private static readonly Regex PlainIdentifierPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_$]*$", RegexOptions.Compiled);

    /// <summary>
    /// 解析索引欄位項目（不含 ASC/DESC）。
    /// 單一識別項回傳欄位名稱（引號識別項去引號；未加引號依 PostgreSQL 規則轉小寫），
    /// 其餘（運算式、含 opclass / COLLATE）原樣回傳並標記為運算式。
    /// </summary>
    public static (string Name, bool IsExpression) ParseColumnToken(string token)
    {
        var trimmed = token.Trim();

        var quoted = QuotedIdentifierPattern.Match(trimmed);
        if (quoted.Success)
            return (quoted.Groups[1].Value.Replace("\"\"", "\""), false);

        if (PlainIdentifierPattern.IsMatch(trimmed))
            return (trimmed.ToLowerInvariant(), false);

        return (trimmed, true);
    }

    /// <summary>
    /// 比較兩個 SQL 片段（運算式或 WHERE 條件）在語意上是否視為相同。
    /// 去除引號外空白、引號外轉小寫、去除包住整段的外層括號後比較。
    /// </summary>
    public static bool FragmentsEqual(string? a, string? b)
    {
        if (a == null || b == null)
            return a == null && b == null;
        return Normalize(a) == Normalize(b);
    }

    /// <summary>
    /// 正規化 SQL 片段，供比對使用（不可用於產生 SQL）。
    /// </summary>
    public static string Normalize(string fragment)
    {
        var sb = new StringBuilder(fragment.Length);
        char quote = '\0';

        foreach (var ch in fragment)
        {
            if (quote != '\0')
            {
                sb.Append(ch);
                if (ch == quote) quote = '\0';
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                quote = ch;
                sb.Append(ch);
                continue;
            }

            if (!char.IsWhiteSpace(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }

        var result = sb.ToString();
        while (IsWrappedInParens(result))
            result = result[1..^1];
        return result;
    }

    /// <summary>判斷整段是否被一組最外層括號完整包住，如 "(a AND b)"；"(a) AND (b)" 則否。</summary>
    private static bool IsWrappedInParens(string s)
    {
        if (s.Length < 2 || s[0] != '(' || s[^1] != ')')
            return false;

        var masked = SqlTextScanner.Mask(s);
        return SqlTextScanner.FindMatchingParen(masked, 0) == s.Length - 1;
    }
}
