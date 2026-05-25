namespace PostgresDeployer.Core.Services;

using System.Text.RegularExpressions;
using PostgresDeployer.Core.Helpers;
using PostgresDeployer.Core.Interfaces;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Resources;

/// <summary>
/// Schema 差異比對引擎。
/// 比較「期望狀態」（SQL 檔案定義）與「實際狀態」（DB 現有 Schema），
/// 產生差異變更清單。
/// </summary>
public class SchemaDiffer : ISchemaDiffer
{
    public List<SchemaChange> ComputeChanges(
        List<TableSchema> desired,
        Dictionary<string, TableSchema> actual)
    {
        var changes = new List<SchemaChange>();

        foreach (var desiredTable in desired)
        {
            if (!actual.TryGetValue(desiredTable.TableName, out var actualTable))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.CreateTable,
                    EntityName = desiredTable.TableName,
                    Description = CoreStrings.Format("Diff_CreateTable",
                        desiredTable.TableName, desiredTable.Columns.Count, desiredTable.Indexes.Count),
                    Sql = desiredTable.RawSql
                });
                continue;
            }

            CompareColumns(desiredTable, actualTable, changes);
            ComparePrimaryKey(desiredTable, actualTable, changes);
            CompareIndexes(desiredTable, actualTable, changes);
            CompareForeignKeys(desiredTable, actualTable, changes);
        }

        return changes;
    }

    private void CompareColumns(TableSchema desired, TableSchema actual, List<SchemaChange> changes)
    {
        var actualCols = actual.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

        foreach (var desiredCol in desired.Columns)
        {
            if (!actualCols.TryGetValue(desiredCol.Name, out var actualCol))
            {
                var addChange = new SchemaChange
                {
                    Type = ChangeType.AddColumn,
                    EntityName = desired.TableName,
                    ColumnName = desiredCol.Name,
                    Description = CoreStrings.Format("Diff_AddColumn",
                        desiredCol.Name, desiredCol.RawType, desiredCol.IsNullable ? " NULL" : " NOT NULL")
                };

                if (!desiredCol.IsNullable && !desiredCol.HasDefault && !desiredCol.IsIdentity)
                {
                    addChange.CautionMessage = CoreStrings.Format("Diff_AddColumn_Caution", desiredCol.Name);
                }

                changes.Add(addChange);
                continue;
            }

            // 型別比較
            var desiredType = TypeNormalizer.ParseSqlType(desiredCol.RawType);
            var actualType = (actualCol.Type, actualCol.Length, actualCol.Precision, actualCol.Scale);

            if (!TypeNormalizer.TypesMatch(desiredType, actualType))
            {
                var alterTypeChange = new SchemaChange
                {
                    Type = ChangeType.AlterColumnType,
                    EntityName = desired.TableName,
                    ColumnName = desiredCol.Name,
                    Description = CoreStrings.Format("Diff_AlterType",
                        desiredCol.Name, actualCol.RawType, desiredCol.RawType)
                };

                if (TypeNormalizer.IsTypeNarrowing(desiredType, actualType))
                {
                    alterTypeChange.CautionMessage = CoreStrings.Format("Diff_AlterType_Caution",
                        desiredCol.Name, actualCol.RawType, desiredCol.RawType);
                }

                changes.Add(alterTypeChange);
            }

            // Nullable 比較
            if (desiredCol.IsNullable != actualCol.IsNullable)
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.AlterColumnNullable,
                    EntityName = desired.TableName,
                    ColumnName = desiredCol.Name,
                    Description = CoreStrings.Format("Diff_AlterNullable",
                        desiredCol.Name,
                        actualCol.IsNullable ? "NULL" : "NOT NULL",
                        desiredCol.IsNullable ? "NULL" : "NOT NULL")
                });
            }

            // Default 比較
            var desiredDefault = DefaultValueNormalizer.NormalizeFromSql(
                desiredCol.HasDefault ? desiredCol.DefaultValue : null);
            var actualDefault = actualCol.HasDefault ? actualCol.DefaultValue : null;

            if (!DefaultValueNormalizer.DefaultsMatch(desiredDefault, actualDefault))
            {
                var none = CoreStrings.Get("Diff_None");
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.AlterColumnDefault,
                    EntityName = desired.TableName,
                    ColumnName = desiredCol.Name,
                    Description = CoreStrings.Format("Diff_AlterDefault",
                        desiredCol.Name, actualDefault ?? none, desiredDefault ?? none)
                });
            }
        }

        // DB 有但 SQL 沒有的欄位 → 刪除
        var desiredColNames = desired.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var actualCol in actual.Columns)
        {
            if (!desiredColNames.Contains(actualCol.Name))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.DropColumn,
                    EntityName = desired.TableName,
                    ColumnName = actualCol.Name,
                    Description = CoreStrings.Format("Diff_DropColumn", actualCol.Name),
                    CautionMessage = CoreStrings.Format("Diff_DropColumn_Caution", actualCol.Name)
                });
            }
        }
    }

    private void ComparePrimaryKey(TableSchema desired, TableSchema actual, List<SchemaChange> changes)
    {
        var desiredPk = desired.PrimaryKey;
        var actualPk = actual.PrimaryKey;

        if (desiredPk == null && actualPk == null)
            return;

        if (desiredPk != null && actualPk == null)
        {
            changes.Add(new SchemaChange
            {
                Type = ChangeType.RecreatePrimaryKey,
                EntityName = desired.TableName,
                Description = CoreStrings.Format("Diff_AddPK",
                    desiredPk.ConstraintName ?? $"PK_{desired.TableName}",
                    string.Join(", ", desiredPk.Columns)),
                CautionMessage = CoreStrings.Get("Diff_AddPK_Caution")
            });
            return;
        }

        if (desiredPk == null && actualPk != null)
        {
            changes.Add(new SchemaChange
            {
                Type = ChangeType.RecreatePrimaryKey,
                EntityName = desired.TableName,
                Description = CoreStrings.Format("Diff_DropPK", actualPk.ConstraintName ?? ""),
                CautionMessage = CoreStrings.Get("Diff_DropPK_Caution")
            });
            return;
        }

        var desiredCols = string.Join(",", desiredPk!.Columns);
        var actualCols = string.Join(",", actualPk!.Columns);

        if (desiredCols != actualCols)
        {
            changes.Add(new SchemaChange
            {
                Type = ChangeType.RecreatePrimaryKey,
                EntityName = desired.TableName,
                Description = CoreStrings.Format("Diff_RecreatePK", actualCols, desiredCols),
                CautionMessage = CoreStrings.Get("Diff_RecreatePK_Caution")
            });
        }
    }

    private void CompareIndexes(TableSchema desired, TableSchema actual, List<SchemaChange> changes)
    {
        var actualIdxMap = actual.Indexes.ToDictionary(i => i.Name, StringComparer.Ordinal);

        foreach (var desiredIdx in desired.Indexes)
        {
            if (!actualIdxMap.TryGetValue(desiredIdx.Name, out var actualIdx))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.CreateIndex,
                    EntityName = desired.TableName,
                    Description = CoreStrings.Format("Diff_CreateIndex", desiredIdx.Name)
                });
            }
            else if (!IndexesMatch(desiredIdx, actualIdx))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.RecreateIndex,
                    EntityName = desired.TableName,
                    Description = CoreStrings.Format("Diff_RecreateIndex", desiredIdx.Name)
                });
            }
        }

        var desiredIdxNames = desired.Indexes.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var actualIdx in actual.Indexes)
        {
            if (!desiredIdxNames.Contains(actualIdx.Name))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.DropIndex,
                    EntityName = desired.TableName,
                    Description = CoreStrings.Format("Diff_DropIndex", actualIdx.Name)
                });
            }
        }
    }

    private static bool IndexesMatch(IndexDefinition a, IndexDefinition b)
    {
        if (a.IsUnique != b.IsUnique) return false;
        if (a.Columns.Count != b.Columns.Count) return false;
        for (int i = 0; i < a.Columns.Count; i++)
        {
            if (a.Columns[i].Name != b.Columns[i].Name) return false;
            if (a.Columns[i].IsDescending != b.Columns[i].IsDescending) return false;
        }
        return true;
    }

    private void CompareForeignKeys(TableSchema desired, TableSchema actual, List<SchemaChange> changes)
    {
        var actualFks = actual.ForeignKeys.ToDictionary(fk => fk.ConstraintName, StringComparer.Ordinal);

        foreach (var desiredFk in desired.ForeignKeys)
        {
            if (!actualFks.TryGetValue(desiredFk.ConstraintName, out var actualFk))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.CreateForeignKey,
                    EntityName = desired.TableName,
                    ColumnName = desiredFk.ConstraintName,
                    Description = CoreStrings.Format("Diff_AddFK",
                        desiredFk.ConstraintName,
                        string.Join(", ", desiredFk.Columns),
                        desiredFk.ReferencedTable)
                });
            }
            else if (!ForeignKeysMatch(desiredFk, actualFk))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.RecreateForeignKey,
                    EntityName = desired.TableName,
                    ColumnName = desiredFk.ConstraintName,
                    Description = CoreStrings.Format("Diff_RecreateFK", desiredFk.ConstraintName),
                    CautionMessage = CoreStrings.Get("Diff_RecreateFK_Caution")
                });
            }
        }

        var desiredFkNames = desired.ForeignKeys.Select(fk => fk.ConstraintName).ToHashSet(StringComparer.Ordinal);
        foreach (var actualFk in actual.ForeignKeys)
        {
            if (!desiredFkNames.Contains(actualFk.ConstraintName))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.DropForeignKey,
                    EntityName = desired.TableName,
                    ColumnName = actualFk.ConstraintName,
                    Description = CoreStrings.Format("Diff_DropFK", actualFk.ConstraintName),
                    CautionMessage = CoreStrings.Format("Diff_DropFK_Caution", actualFk.ConstraintName)
                });
            }
        }
    }

    private static bool ForeignKeysMatch(ForeignKeyDefinition a, ForeignKeyDefinition b)
    {
        if (!string.Equals(a.ReferencedTable, b.ReferencedTable, StringComparison.Ordinal)) return false;
        if (a.Columns.Count != b.Columns.Count) return false;
        if (a.ReferencedColumns.Count != b.ReferencedColumns.Count) return false;
        for (int i = 0; i < a.Columns.Count; i++)
        {
            if (!string.Equals(a.Columns[i], b.Columns[i], StringComparison.Ordinal)) return false;
            if (!string.Equals(a.ReferencedColumns[i], b.ReferencedColumns[i], StringComparison.Ordinal)) return false;
        }
        static string Norm(string? s) =>
            string.IsNullOrEmpty(s) ? "NO ACTION" : s.ToUpperInvariant().Trim();
        if (Norm(a.OnDelete) != Norm(b.OnDelete)) return false;
        if (Norm(a.OnUpdate) != Norm(b.OnUpdate)) return false;
        return true;
    }

    public List<SchemaChange> ComputeViewChanges(
        List<(string ViewName, string SqlContent)> desiredViews,
        Dictionary<string, List<string>> existingViewColumns)
    {
        var changes = new List<SchemaChange>();

        foreach (var (viewName, sqlContent) in desiredViews)
        {
            if (!existingViewColumns.TryGetValue(viewName, out var existingCols))
            {
                // View 不存在於 DB → 建立
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.CreateView,
                    EntityName = viewName,
                    Description = CoreStrings.Format("Diff_CreateView", viewName),
                    Sql = sqlContent
                });
            }
            else
            {
                // View 已存在：比對欄位名稱清單
                var desiredCols = ExtractDesiredColumnNames(sqlContent);

                // 若無法解析欄位（如 SELECT *），跳過（不標記為變更）
                if (desiredCols == null)
                    continue;

                bool columnsChanged =
                    desiredCols.Count != existingCols.Count ||
                    desiredCols.Zip(existingCols).Any(pair =>
                        !string.Equals(pair.First, pair.Second, StringComparison.Ordinal));

                if (columnsChanged)
                {
                    changes.Add(new SchemaChange
                    {
                        Type = ChangeType.ReplaceView,
                        EntityName = viewName,
                        Description = CoreStrings.Format("Diff_ReplaceView", viewName),
                        Sql = sqlContent
                    });
                }
                // 欄位未變更 → 不標記為 ReplaceView，Orchestrator 仍會以 CREATE OR REPLACE VIEW 同步 SQL 主體
            }
        }

        return changes;
    }

    // ── View 欄位解析輔助 ──────────────────────────────────────────────────

    private static readonly Regex CreateViewPrefixRegex = new(
        @"CREATE\s+(?:OR\s+REPLACE\s+)?VIEW\s+(?:""[^""]*""\s*\.\s*)?""[^""]*""\s+AS\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex AsAliasQuotedRegex =
        new(@"\bAS\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AsAliasUnquotedRegex =
        new(@"\bAS\s+(\w+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LastQuotedNameRegex =
        new(@"""([^""]+)""\s*$", RegexOptions.Compiled);

    private static readonly Regex LastUnquotedWordRegex =
        new(@"\b([A-Za-z_]\w*)\s*$", RegexOptions.Compiled);

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "CASE", "WHEN", "THEN", "ELSE", "END",
        "NULL", "TRUE", "FALSE", "AND", "OR", "NOT", "IN", "LIKE",
        "BETWEEN", "EXISTS", "DISTINCT", "ALL", "ANY"
    };

    /// <summary>
    /// 從 SQL 檔案內容中提取期望的 View 輸出欄位名稱清單。
    /// 若 SQL 包含 SELECT * 或無法解析，回傳 null（呼叫端應跳過比對）。
    /// </summary>
    public static List<string>? ExtractDesiredColumnNames(string sqlContent)
    {
        // 取得 AS 後的 SELECT 主體
        var m = CreateViewPrefixRegex.Match(sqlContent);
        if (!m.Success) return null;

        var body = sqlContent[(m.Index + m.Length)..].Trim().TrimEnd(';').Trim();
        if (string.IsNullOrWhiteSpace(body)) return null;

        // 去掉 SELECT 關鍵字
        if (!body.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return null;

        body = body[6..].TrimStart(); // 移除 "SELECT "

        // SELECT * 無法解析
        if (body.StartsWith("*") || body.StartsWith("DISTINCT *", StringComparison.OrdinalIgnoreCase))
            return null;

        // 找到頂層 FROM 位置，取出 SELECT 清單
        var selectList = ExtractSelectList(body);
        if (selectList == null) return null;

        // 分割頂層逗號
        var parts = SplitTopLevel(selectList);
        if (parts.Count == 0) return null;

        var names = new List<string>();
        foreach (var part in parts)
        {
            var name = ExtractColumnName(part.Trim());
            if (name == null) return null; // 無法解析 → 放棄整個清單
            names.Add(name);
        }
        return names;
    }

    /// <summary>提取 SELECT 清單（FROM 關鍵字之前的部分），正確跳過字串字面量</summary>
    private static string? ExtractSelectList(string body)
    {
        int depth = 0;
        bool inStr = false;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\'' && !inStr) { inStr = true; continue; }
            if (c == '\'' && inStr)
            {
                // 處理逸脫的單引號 '' 
                if (i + 1 < body.Length && body[i + 1] == '\'') { i++; continue; }
                inStr = false;
                continue;
            }
            if (inStr) continue;
            if (c == '(' || c == '[') { depth++; continue; }
            if (c == ')' || c == ']') { depth--; continue; }
            if (depth == 0 && i + 4 <= body.Length)
            {
                var segment = body[i..];
                if (segment.StartsWith("FROM", StringComparison.OrdinalIgnoreCase) &&
                    (i == 0 || !char.IsLetterOrDigit(body[i - 1])))
                {
                    return body[..i].Trim();
                }
            }
        }
        return body.Trim(); // 沒有 FROM（少見但合法）
    }

    /// <summary>依頂層逗號分割，不分割括號內或字串字面量內的逗號</summary>
    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;
        bool inStr = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\'' && !inStr) { inStr = true; current.Append(c); continue; }
            if (c == '\'' && inStr)
            {
                current.Append(c);
                if (i + 1 < text.Length && text[i + 1] == '\'') { i++; current.Append('\''); }
                else inStr = false;
                continue;
            }
            if (inStr) { current.Append(c); continue; }

            if (c == '(' || c == '[') { depth++; current.Append(c); }
            else if (c == ')' || c == ']') { depth--; current.Append(c); }
            else if (c == ',' && depth == 0)
            {
                var part = current.ToString().Trim();
                if (part.Length > 0) parts.Add(part);
                current.Clear();
            }
            else current.Append(c);
        }

        var last = current.ToString().Trim();
        if (last.Length > 0) parts.Add(last);
        return parts;
    }

    /// <summary>從單一欄位表達式中提取輸出欄位名稱</summary>
    private static string? ExtractColumnName(string expr)
    {
        // AS "alias"
        var m = AsAliasQuotedRegex.Match(expr);
        if (m.Success) return m.Groups[1].Value;

        // AS alias（不帶引號）
        m = AsAliasUnquotedRegex.Match(expr);
        if (m.Success) return m.Groups[1].Value;

        // table."column" 或 "column"（最後一個引號識別符）
        m = LastQuotedNameRegex.Match(expr);
        if (m.Success) return m.Groups[1].Value;

        // 最後一個不帶引號的識別符（排除 SQL 關鍵字）
        m = LastUnquotedWordRegex.Match(expr);
        if (m.Success && !SqlKeywords.Contains(m.Groups[1].Value))
            return m.Groups[1].Value;

        return null;
    }
}
