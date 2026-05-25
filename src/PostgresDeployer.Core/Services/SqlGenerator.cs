namespace PostgresDeployer.Core.Services;

using System.Text;
using PostgresDeployer.Core.Interfaces;
using PostgresDeployer.Core.Models;

/// <summary>
/// SQL 語句產生器。
/// 根據 Schema 差異產生對應的 ALTER/CREATE/DROP SQL 語句。
/// 所有表名和欄位名都使用雙引號（PostgreSQL 引號識別項）。
/// </summary>
public class SqlGenerator : ISqlGenerator
{
    public string GenerateAddColumn(string tableName, ColumnDefinition column)
    {
        var sb = new StringBuilder();
        sb.Append($"ALTER TABLE \"{tableName}\" ADD COLUMN \"{column.Name}\" {column.RawType}");

        if (!column.IsNullable)
            sb.Append(" NOT NULL");

        if (column.IsIdentity)
            sb.Append(" GENERATED ALWAYS AS IDENTITY");
        else if (column.HasDefault)
            sb.Append($" DEFAULT {column.DefaultValue}");

        sb.Append(';');
        return sb.ToString();
    }

    public string GenerateAlterColumnType(string tableName, string columnName, string newRawType)
    {
        return $"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" TYPE {newRawType} USING \"{columnName}\"::{newRawType};";
    }

    public string GenerateAlterColumnNullable(string tableName, string columnName, bool nullable)
    {
        var action = nullable ? "DROP NOT NULL" : "SET NOT NULL";
        return $"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" {action};";
    }

    public string GenerateAlterColumnDefault(string tableName, string columnName, string? defaultValue)
    {
        if (defaultValue == null)
            return $"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" DROP DEFAULT;";

        return $"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" SET DEFAULT {defaultValue};";
    }

    public string GenerateCreateIndex(string tableName, IndexDefinition index)
    {
        var unique = index.IsUnique ? "UNIQUE " : "";
        var cols = string.Join(", ",
            index.Columns.Select(c =>
                $"\"{c.Name}\"{(c.IsDescending ? " DESC" : "")}"));

        return $"CREATE {unique}INDEX IF NOT EXISTS \"{index.Name}\" ON \"{tableName}\" ({cols});";
    }

    public string GenerateDropIndex(string indexName)
    {
        return $"DROP INDEX IF EXISTS \"{indexName}\";";
    }

    public string GenerateDropColumn(string tableName, string columnName)
    {
        return $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\";";
    }

    public string GenerateDropPrimaryKey(string tableName, string constraintName)
    {
        return $"ALTER TABLE \"{tableName}\" DROP CONSTRAINT \"{constraintName}\";";
    }

    public string GenerateCreatePrimaryKey(string tableName, PrimaryKeyDefinition pk)
    {
        var cols = string.Join(", ", pk.Columns.Select(c => $"\"{c}\""));
        var constraintName = pk.ConstraintName ?? $"PK_{tableName}";
        return $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{constraintName}\" PRIMARY KEY ({cols});";
    }

    public string GenerateAddForeignKey(string tableName, ForeignKeyDefinition fk)
    {
        var cols    = string.Join(", ", fk.Columns.Select(c => $"\"{c}\""));
        var refCols = string.Join(", ", fk.ReferencedColumns.Select(c => $"\"{c}\""));
        var sb = new StringBuilder();
        sb.Append($"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{fk.ConstraintName}\" ");
        sb.Append($"FOREIGN KEY ({cols}) REFERENCES \"{fk.ReferencedTable}\" ({refCols})");
        var onDelete = (fk.OnDelete ?? "NO ACTION").ToUpperInvariant().Trim();
        var onUpdate = (fk.OnUpdate ?? "NO ACTION").ToUpperInvariant().Trim();
        if (onDelete != "NO ACTION") sb.Append($" ON DELETE {onDelete}");
        if (onUpdate != "NO ACTION") sb.Append($" ON UPDATE {onUpdate}");
        sb.Append(';');
        return sb.ToString();
    }

    public string GenerateDropForeignKey(string tableName, string constraintName)
    {
        return $"ALTER TABLE \"{tableName}\" DROP CONSTRAINT \"{constraintName}\";";
    }
}
