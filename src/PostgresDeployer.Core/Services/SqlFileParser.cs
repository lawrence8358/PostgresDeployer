namespace PostgresDeployer.Core.Services;

using System.Text.RegularExpressions;
using PostgresDeployer.Core.Helpers;
using PostgresDeployer.Core.Interfaces;
using PostgresDeployer.Core.Models;

/// <summary>
/// SQL 檔案解析器。
/// 從 PostgreSQL CREATE TABLE SQL 檔案中擷取結構化 Schema 資訊。
/// </summary>
public class SqlFileParser : ISqlFileParser
{
    private static readonly Regex TableNamePattern = new(
        @"CREATE\s+TABLE\s+(IF\s+NOT\s+EXISTS\s+)?""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IndexPattern = new(
        @"CREATE\s+(UNIQUE\s+)?INDEX\s+(IF\s+NOT\s+EXISTS\s+)?""([^""]+)""\s+ON\s+(?:[a-zA-Z_]\w*\.)?""([^""]+)""\s*\(([^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ColumnNamePattern = new(
        @"^\s*""([^""]+)""\s+(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex PkConstraintPattern = new(
        @"(?:CONSTRAINT\s+""([^""]+)""\s+)?PRIMARY\s+KEY\s*\(([^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NotNullPattern = new(
        @"\bNOT\s+NULL\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IdentityPattern = new(
        @"\bGENERATED\s+ALWAYS\s+AS\s+IDENTITY\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InlinePkPattern = new(
        @"\bPRIMARY\s+KEY\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DefaultPattern = new(
        @"\bDEFAULT\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ForeignKeyKeywordPattern = new(
        @"\bFOREIGN\s+KEY\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FkConstraintPattern = new(
        @"CONSTRAINT\s+""([^""]+)""\s+FOREIGN\s+KEY\s*\(([^)]+)\)\s+REFERENCES\s+""([^""]+)""\s*\(([^)]+)\)(?:\s+ON\s+DELETE\s+(CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION))?(?:\s+ON\s+UPDATE\s+(CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TableCommentPattern = new(
        @"COMMENT\s+ON\s+TABLE\s+(?:(?:""[^""]+""|\w+)\.)?""(?<table>[^""]+)""\s+IS\s+(?<value>NULL|'(?:''|[^'])*')\s*;",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ColumnCommentPattern = new(
        @"COMMENT\s+ON\s+COLUMN\s+(?:(?:""[^""]+""|\w+)\.)?""(?<table>[^""]+)""\.""(?<column>[^""]+)""\s+IS\s+(?<value>NULL|'(?:''|[^'])*')\s*;",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TableSchema ParseTableFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return ParseTableSql(content);
    }

    public List<TableSchema> ParseTableDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        return Directory.GetFiles(directoryPath, "*.sql")
            .OrderBy(f => Path.GetFileName(f))
            .Select(ParseTableFile)
            .ToList();
    }

    public List<(string FileName, string Content)> ReadSqlFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        return Directory.GetFiles(directoryPath, "*.sql", SearchOption.AllDirectories)
            .OrderBy(f => Path.GetRelativePath(directoryPath, f))
            .Select(f => (Path.GetRelativePath(directoryPath, f), File.ReadAllText(f)))
            .ToList();
    }

    /// <summary>
    /// 解析 CREATE TABLE SQL 文字，回傳 TableSchema。
    /// </summary>
    public TableSchema ParseTableSql(string sql)
    {
        var schema = new TableSchema { RawSql = sql };

        // ═══ Step 1: 擷取表名 ═══
        var tableMatch = TableNamePattern.Match(sql);
        if (!tableMatch.Success)
            throw new InvalidOperationException("無法從 SQL 中解析表名。SQL 必須包含 CREATE TABLE \"TableName\" 語法。");

        schema.IfNotExists = tableMatch.Groups[1].Success;
        schema.TableName = tableMatch.Groups[2].Value;

        // ═══ Step 2: 擷取 CREATE TABLE (...) 區塊 ═══
        var openParenIndex = sql.IndexOf('(', tableMatch.Index + tableMatch.Length);
        if (openParenIndex < 0)
            throw new InvalidOperationException($"表 \"{schema.TableName}\" 的 CREATE TABLE 語法缺少左括號。");

        var bodyContent = ExtractParenthesizedBlock(sql, openParenIndex);

        // ═══ Step 3: 分割欄位行 ═══
        var lines = SplitAtTopLevelCommas(bodyContent);

        // ═══ Step 4: 逐行解析 ═══
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            if (trimmedLine.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase)
                || trimmedLine.StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase))
            {
                if (ForeignKeyKeywordPattern.IsMatch(trimmedLine))
                    ParseForeignKeyConstraint(trimmedLine, schema);
                else
                    ParsePrimaryKeyConstraint(trimmedLine, schema);
            }
            else if (trimmedLine.StartsWith("\""))
            {
                var col = ParseColumn(trimmedLine);
                if (col != null)
                {
                    schema.Columns.Add(col.Value.Column);
                    if (col.Value.IsInlinePk)
                    {
                        schema.PrimaryKey = new PrimaryKeyDefinition
                        {
                            Columns = [col.Value.Column.Name]
                        };
                    }
                }
            }
        }

        // ═══ Step 5: 解析 CREATE INDEX ═══
        var closeParenIndex = openParenIndex + bodyContent.Length + 2; // +2 for the parens
        var afterTable = closeParenIndex < sql.Length ? sql[closeParenIndex..] : "";

        foreach (Match idxMatch in IndexPattern.Matches(afterTable))
        {
            var isUnique = idxMatch.Groups[1].Success;
            var indexName = idxMatch.Groups[3].Value;
            var indexCols = ParseIndexColumns(idxMatch.Groups[5].Value);

            schema.Indexes.Add(new IndexDefinition
            {
                Name = indexName,
                IsUnique = isUnique,
                Columns = indexCols
            });
        }

        ParseComments(sql, schema);

        return schema;
    }

    /// <summary>
    /// 擷取括號區塊內容（不含外層括號）。
    /// </summary>
    private static string ExtractParenthesizedBlock(string sql, int openParenIndex)
    {
        int depth = 0;
        bool inQuote = false;

        for (int i = openParenIndex; i < sql.Length; i++)
        {
            var ch = sql[i];

            if (ch == '\'')
            {
                if (inQuote)
                {
                    // Handle escaped quote ('')
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        i++; // Skip escaped quote
                    else
                        inQuote = false;
                }
                else
                {
                    inQuote = true;
                }
                continue;
            }

            if (!inQuote)
            {
                if (ch == '(') depth++;
                else if (ch == ')')
                {
                    depth--;
                    if (depth == 0)
                        return sql[(openParenIndex + 1)..i];
                }
            }
        }

        throw new InvalidOperationException("無法找到匹配的右括號。");
    }

    /// <summary>
    /// 在最外層（depth=0）的逗號處分割字串。
    /// </summary>
    private static List<string> SplitAtTopLevelCommas(string content)
    {
        var result = new List<string>();
        int depth = 0;
        bool inQuote = false;
        int start = 0;

        for (int i = 0; i < content.Length; i++)
        {
            var ch = content[i];

            if (ch == '\'')
            {
                if (inQuote)
                {
                    // Handle escaped quote ('')
                    if (i + 1 < content.Length && content[i + 1] == '\'')
                        i++; // Skip escaped quote
                    else
                        inQuote = false;
                }
                else
                {
                    inQuote = true;
                }
                continue;
            }

            if (!inQuote)
            {
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (ch == ',' && depth == 0)
                {
                    result.Add(content[start..i]);
                    start = i + 1;
                }
            }
        }

        if (start < content.Length)
            result.Add(content[start..]);

        return result;
    }

    /// <summary>
    /// 解析單一欄位定義行。
    /// </summary>
    private (ColumnDefinition Column, bool IsInlinePk)? ParseColumn(string line)
    {
        var match = ColumnNamePattern.Match(line);
        if (!match.Success) return null;

        var columnName = match.Groups[1].Value;
        var remainder = match.Groups[2].Value.Trim();

        // 擷取型別：掃描到 depth=0 的空白處截斷
        var rawType = ExtractType(remainder, out var afterType);

        var typeInfo = TypeNormalizer.ParseSqlType(rawType);

        var column = new ColumnDefinition
        {
            Name = columnName,
            Type = typeInfo.BaseType,
            RawType = rawType,
            Length = typeInfo.Length,
            Precision = typeInfo.Precision,
            Scale = typeInfo.Scale,
        };

        // 解析修飾詞
        var modifiers = afterType.Trim();
        bool isInlinePk = false;

        // NOT NULL
        if (NotNullPattern.IsMatch(modifiers))
            column.IsNullable = false;

        // GENERATED ALWAYS AS IDENTITY
        if (IdentityPattern.IsMatch(modifiers))
            column.IsIdentity = true;

        // PRIMARY KEY (inline)
        if (InlinePkPattern.IsMatch(modifiers))
            isInlinePk = true;

        // DEFAULT value
        var defaultMatch = DefaultPattern.Match(modifiers);
        if (defaultMatch.Success)
        {
            var defaultStart = defaultMatch.Index + defaultMatch.Length;
            var defaultValue = ExtractDefaultValue(modifiers, defaultStart);
            column.HasDefault = true;
            column.DefaultValue = DefaultValueNormalizer.NormalizeFromSql(defaultValue);
        }

        return (column, isInlinePk);
    }

    /// <summary>
    /// 從餘下文字中擷取型別 token（含括號如 VARCHAR(200)、DECIMAL(18,10)）。
    /// </summary>
    private static string ExtractType(string remainder, out string afterType)
    {
        int depth = 0;
        int i;
        for (i = 0; i < remainder.Length; i++)
        {
            var ch = remainder[i];
            if (ch == '(') depth++;
            else if (ch == ')') depth--;
            else if (char.IsWhiteSpace(ch) && depth == 0)
                break;
        }

        afterType = i < remainder.Length ? remainder[i..] : "";
        return remainder[..i];
    }

    /// <summary>
    /// 從 DEFAULT 關鍵字後擷取預設值。
    /// </summary>
    private static string ExtractDefaultValue(string text, int startIndex)
    {
        if (startIndex >= text.Length)
            return "";

        var sub = text[startIndex..].TrimStart();
        if (sub.Length == 0)
            return "";

        // 字串常數：'xxx'（支援跳脫引號 ''）
        if (sub[0] == '\'')
        {
            for (int i = 1; i < sub.Length; i++)
            {
                if (sub[i] == '\'')
                {
                    // Handle escaped quote ('')
                    if (i + 1 < sub.Length && sub[i + 1] == '\'')
                    {
                        i++; // Skip escaped quote
                        continue;
                    }
                    return sub[..(i + 1)];
                }
            }
            return sub;
        }

        // 函數呼叫：xxx(...)
        var parenIdx = sub.IndexOf('(');
        var spaceIdx = FindNextKeywordOrEnd(sub);

        if (parenIdx >= 0 && (spaceIdx < 0 || parenIdx < spaceIdx))
        {
            // Find matching close paren
            int depth = 0;
            for (int i = parenIdx; i < sub.Length; i++)
            {
                if (sub[i] == '(') depth++;
                else if (sub[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                        return sub[..(i + 1)];
                }
            }
            return sub;
        }

        // Simple token (FALSE, TRUE, 0, etc.)
        var end = FindNextKeywordOrEnd(sub);
        return end >= 0 ? sub[..end].TrimEnd() : sub.TrimEnd();
    }

    /// <summary>
    /// 找到下一個 SQL 關鍵字（NOT, NULL, GENERATED, PRIMARY, CONSTRAINT）或字串結尾。
    /// </summary>
    private static int FindNextKeywordOrEnd(string text)
    {
        // Search for common SQL keywords that follow DEFAULT value
        string[] keywords = ["NOT", "NULL", "GENERATED", "PRIMARY", "CONSTRAINT"];
        int minIndex = -1;

        foreach (var kw in keywords)
        {
            var idx = 0;
            while (idx < text.Length)
            {
                idx = text.IndexOf(kw, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                // Ensure it's a word boundary
                bool leftBound = idx == 0 || char.IsWhiteSpace(text[idx - 1]);
                bool rightBound = idx + kw.Length >= text.Length || char.IsWhiteSpace(text[idx + kw.Length]);

                if (leftBound && rightBound)
                {
                    if (minIndex < 0 || idx < minIndex)
                        minIndex = idx;
                    break;
                }
                idx++;
            }
        }

        return minIndex;
    }

    /// <summary>
    /// 解析 FOREIGN KEY 約束行，加入 schema.ForeignKeys。
    /// </summary>
    private static void ParseForeignKeyConstraint(string line, TableSchema schema)
    {
        var match = FkConstraintPattern.Match(line);
        if (!match.Success) return;

        var constraintName = match.Groups[1].Value;
        var columns = ParseQuotedColumnNames(match.Groups[2].Value);
        var referencedTable = match.Groups[3].Value;
        var referencedColumns = ParseQuotedColumnNames(match.Groups[4].Value);

        // 正規化 "SET NULL" / "SET DEFAULT"（去除多餘空白）
        static string NormalizeAction(string? s) =>
            string.IsNullOrEmpty(s) ? "NO ACTION"
            : System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").ToUpperInvariant().Trim();

        schema.ForeignKeys.Add(new ForeignKeyDefinition
        {
            ConstraintName = constraintName,
            Columns = columns,
            ReferencedTable = referencedTable,
            ReferencedColumns = referencedColumns,
            OnDelete = NormalizeAction(match.Groups[5].Success ? match.Groups[5].Value : null),
            OnUpdate = NormalizeAction(match.Groups[6].Success ? match.Groups[6].Value : null),
        });
    }

    /// <summary>
    /// 從 SQL 識別項清單（如 "col1", "col2"）擷取不含引號的名稱清單。
    /// </summary>
    private static List<string> ParseQuotedColumnNames(string namesText)
    {
        return Regex.Matches(namesText, @"""([^""]+)""")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    /// <summary>
    /// 解析 PRIMARY KEY 約束行。
    /// </summary>
    private static void ParsePrimaryKeyConstraint(string line, TableSchema schema)
    {
        var match = PkConstraintPattern.Match(line);
        if (!match.Success) return;

        var constraintName = match.Groups[1].Success ? match.Groups[1].Value : null;
        var columnsText = match.Groups[2].Value;

        var columns = columnsText.Split(',')
            .Select(c => c.Trim().Trim('"'))
            .Where(c => c.Length > 0)
            .ToList();

        schema.PrimaryKey = new PrimaryKeyDefinition
        {
            ConstraintName = constraintName,
            Columns = columns
        };
    }

    /// <summary>
    /// 解析索引欄位清單。
    /// </summary>
    private static List<IndexColumn> ParseIndexColumns(string columnsText)
    {
        return columnsText.Split(',')
            .Select(c =>
            {
                var trimmed = c.Trim();
                var isDesc = trimmed.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase);
                var name = trimmed
                    .Replace(" DESC", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ASC", "", StringComparison.OrdinalIgnoreCase)
                    .Trim()
                    .Trim('"');

                return new IndexColumn { Name = name, IsDescending = isDesc };
            })
            .Where(c => c.Name.Length > 0)
            .ToList();
    }

    private static void ParseComments(string sql, TableSchema schema)
    {
        foreach (Match match in TableCommentPattern.Matches(sql))
        {
            if (string.Equals(match.Groups["table"].Value, schema.TableName, StringComparison.Ordinal))
                schema.Comment = ParseCommentValue(match.Groups["value"].Value);
        }

        var columnsByName = schema.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        foreach (Match match in ColumnCommentPattern.Matches(sql))
        {
            if (!string.Equals(match.Groups["table"].Value, schema.TableName, StringComparison.Ordinal))
                continue;

            if (columnsByName.TryGetValue(match.Groups["column"].Value, out var column))
                column.Comment = ParseCommentValue(match.Groups["value"].Value);
        }
    }

    private static string? ParseCommentValue(string sqlLiteral)
    {
        if (sqlLiteral.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return null;

        if (sqlLiteral.Length >= 2 && sqlLiteral[0] == '\'' && sqlLiteral[^1] == '\'')
            return sqlLiteral[1..^1].Replace("''", "'");

        return sqlLiteral;
    }
}
