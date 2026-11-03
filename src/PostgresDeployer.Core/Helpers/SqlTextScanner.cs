namespace PostgresDeployer.Core.Helpers;

using System.Text.RegularExpressions;

/// <summary>
/// SQL 文字結構掃描工具。
/// 提供「遮蔽」（Mask）功能：把註解、字串常值、引號識別項與 dollar-quote 的內容換成空白，
/// 讓呼叫端可以安全地搜尋分號、括號與關鍵字，且遮蔽後字串與原文長度相同、位置一一對應。
/// </summary>
public static class SqlTextScanner
{
    private static readonly Regex DollarTagPattern = new(
        @"\G\$(?:[A-Za-z_][A-Za-z0-9_]*)?\$", RegexOptions.Compiled);

    /// <summary>
    /// 在陳述式中間（行首、括號外）出現時，代表前一個陳述式很可能漏了分號的關鍵字。
    /// </summary>
    private static readonly Regex StatementStartPattern = new(
        @"\G(?:CREATE|COMMENT\s+ON|ALTER\s+TABLE|INSERT\s+INTO|DROP\s+(?:TABLE|INDEX|VIEW))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 回傳與原文等長的字串：註解、字串常值、引號識別項、dollar-quote 的「內容」以空白取代（保留換行與引號字元）。
    /// </summary>
    public static string Mask(string sql)
    {
        var chars = sql.ToCharArray();
        int n = sql.Length;
        int i = 0;

        void Blank(int k)
        {
            if (chars[k] != '\n' && chars[k] != '\r')
                chars[k] = ' ';
        }

        while (i < n)
        {
            var c = sql[i];

            // 單行註解 -- ...
            if (c == '-' && i + 1 < n && sql[i + 1] == '-')
            {
                while (i < n && sql[i] != '\n')
                    Blank(i++);
                continue;
            }

            // 區塊註解 /* ... */（PostgreSQL 允許巢狀）
            if (c == '/' && i + 1 < n && sql[i + 1] == '*')
            {
                int depth = 0;
                while (i < n)
                {
                    if (sql[i] == '/' && i + 1 < n && sql[i + 1] == '*')
                    {
                        depth++;
                        Blank(i); Blank(i + 1);
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '*' && i + 1 < n && sql[i + 1] == '/')
                    {
                        depth--;
                        Blank(i); Blank(i + 1);
                        i += 2;
                        if (depth == 0) break;
                        continue;
                    }
                    Blank(i++);
                }
                continue;
            }

            // 字串常值 '...' 與引號識別項 "..."（重複引號為跳脫）
            if (c == '\'' || c == '"')
            {
                i++;
                while (i < n)
                {
                    if (sql[i] == c)
                    {
                        if (i + 1 < n && sql[i + 1] == c)
                        {
                            Blank(i); Blank(i + 1);
                            i += 2;
                            continue;
                        }
                        break;
                    }
                    Blank(i++);
                }
                i++; // 跳過結尾引號
                continue;
            }

            // Dollar-quote：$$...$$ 或 $tag$...$tag$
            if (c == '$' && (i == 0 || !IsIdentifierChar(sql[i - 1])))
            {
                var m = DollarTagPattern.Match(sql, i);
                if (m.Success)
                {
                    var tag = m.Value;
                    var bodyStart = i + tag.Length;
                    var end = sql.IndexOf(tag, bodyStart, StringComparison.Ordinal);
                    var stop = end < 0 ? n : end;
                    for (int k = bodyStart; k < stop; k++)
                        Blank(k);
                    i = end < 0 ? n : end + tag.Length;
                    continue;
                }
            }

            i++;
        }

        return new string(chars);
    }

    /// <summary>
    /// 以分號切割 SQL 為多個陳述式（忽略註解、字串、dollar-quote 中的分號）。
    /// 每個陳述式的 <see cref="SqlStatement.Start"/> 指向第一個非空白、非註解字元。
    /// </summary>
    public static List<SqlStatement> SplitStatements(string sql)
    {
        var masked = Mask(sql);
        var result = new List<SqlStatement>();
        int segStart = 0;

        for (int i = 0; i <= masked.Length; i++)
        {
            if (i < masked.Length && masked[i] != ';')
                continue;

            int start = segStart;
            while (start < i && char.IsWhiteSpace(masked[start])) start++;
            int end = i;
            while (end > start && char.IsWhiteSpace(masked[end - 1])) end--;

            if (end > start)
                result.Add(new SqlStatement(start, sql[start..end], masked[start..end]));

            segStart = i + 1;
        }

        return result;
    }

    /// <summary>
    /// 從遮蔽後文字中 <paramref name="openIndex"/> 位置的左括號開始，找出對應的右括號位置；找不到回傳 -1。
    /// </summary>
    public static int FindMatchingParen(string masked, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < masked.Length; i++)
        {
            if (masked[i] == '(') depth++;
            else if (masked[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 以最外層逗號切割。以遮蔽後文字判斷切割位置與前後空白（註解視為空白），
    /// 回傳對應的原文與遮蔽後片段（兩者等長）。
    /// </summary>
    public static List<(string Text, string Masked)> SplitTopLevelCommas(string text, string masked)
    {
        var parts = new List<(string Text, string Masked)>();
        int depth = 0;
        int start = 0;

        void Add(int from, int to)
        {
            while (from < to && char.IsWhiteSpace(masked[from])) from++;
            while (to > from && char.IsWhiteSpace(masked[to - 1])) to--;
            if (to > from)
                parts.Add((text[from..to], masked[from..to]));
        }

        for (int i = 0; i < masked.Length; i++)
        {
            if (masked[i] == '(') depth++;
            else if (masked[i] == ')') depth--;
            else if (masked[i] == ',' && depth == 0)
            {
                Add(start, i);
                start = i + 1;
            }
        }
        Add(start, masked.Length);
        return parts;
    }

    /// <summary>
    /// 找出「陳述式中間另起新陳述式」的位置（行首、括號外出現 CREATE / COMMENT ON / ALTER TABLE 等），
    /// 這通常代表前一個陳述式結尾漏了分號。回傳原文中的位置與關鍵字；沒有則回傳 null。
    /// </summary>
    public static (int Offset, string Keyword)? FindMissingTerminator(string sql)
    {
        foreach (var stmt in SplitStatements(sql))
        {
            var masked = stmt.Masked;
            int depth = 0;
            bool lineStart = false;

            for (int i = 0; i < masked.Length; i++)
            {
                var ch = masked[i];
                if (ch == '\n')
                {
                    lineStart = true;
                    continue;
                }
                if (lineStart && (ch == ' ' || ch == '\t' || ch == '\r'))
                    continue;

                if (lineStart && depth == 0)
                {
                    var m = StatementStartPattern.Match(masked, i);
                    if (m.Success)
                        return (stmt.Start + i, Regex.Replace(m.Value, @"\s+", " ").ToUpperInvariant());
                }
                lineStart = false;

                if (ch == '(') depth++;
                else if (ch == ')') depth--;
            }
        }
        return null;
    }

    /// <summary>取得字元位置所在的行號（1-based）。</summary>
    public static int GetLineNumber(string text, int offset)
    {
        int line = 1;
        var end = Math.Min(offset, text.Length);
        for (int i = 0; i < end; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}

/// <summary>
/// 切割後的單一 SQL 陳述式（不含結尾分號）。
/// </summary>
/// <param name="Start">陳述式在原文中的起始位置</param>
/// <param name="Text">陳述式原文</param>
/// <param name="Masked">陳述式遮蔽後文字（與 Text 等長）</param>
public readonly record struct SqlStatement(int Start, string Text, string Masked);
