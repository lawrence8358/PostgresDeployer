namespace PostgresDeployer.Core.Services;

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
                    TableName = desiredTable.TableName,
                    Description = CoreStrings.Format("Diff_CreateTable",
                        desiredTable.TableName, desiredTable.Columns.Count, desiredTable.Indexes.Count),
                    Sql = desiredTable.RawSql
                });
                continue;
            }

            CompareColumns(desiredTable, actualTable, changes);
            ComparePrimaryKey(desiredTable, actualTable, changes);
            CompareIndexes(desiredTable, actualTable, changes);
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
                    TableName = desired.TableName,
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
                    TableName = desired.TableName,
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
                    TableName = desired.TableName,
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
                    TableName = desired.TableName,
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
                    TableName = desired.TableName,
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
                TableName = desired.TableName,
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
                TableName = desired.TableName,
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
                TableName = desired.TableName,
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
                    TableName = desired.TableName,
                    Description = CoreStrings.Format("Diff_CreateIndex", desiredIdx.Name)
                });
            }
            else if (!IndexesMatch(desiredIdx, actualIdx))
            {
                changes.Add(new SchemaChange
                {
                    Type = ChangeType.RecreateIndex,
                    TableName = desired.TableName,
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
                    TableName = desired.TableName,
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
}
