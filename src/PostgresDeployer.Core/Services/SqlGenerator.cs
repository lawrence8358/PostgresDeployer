namespace PostgresDeployer.Core.Services;

using System.Text;
using System.Text.RegularExpressions;
using PostgresDeployer.Core.Helpers;
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

    public string GenerateAlterTableComment(string tableName, string? comment)
    {
        return $"COMMENT ON TABLE \"{tableName}\" IS {ToSqlCommentLiteral(comment)};";
    }

    public string GenerateAlterColumnComment(string tableName, string columnName, string? comment)
    {
        return $"COMMENT ON COLUMN \"{tableName}\".\"{columnName}\" IS {ToSqlCommentLiteral(comment)};";
    }

    public string GenerateCreateIndex(string tableName, IndexDefinition index)
    {
        // 來自 SQL 定義檔的索引：原樣執行，保留 WHERE / INCLUDE / WITH / opclass 等所有子句
        if (!string.IsNullOrWhiteSpace(index.Definition))
        {
            var definition = RemoveConcurrently(index.Definition.TrimEnd());
            return definition.EndsWith(';') ? definition : definition + ";";
        }

        var unique = index.IsUnique ? "UNIQUE " : "";
        var method = string.IsNullOrEmpty(index.Method) || index.Method.Equals("btree", StringComparison.OrdinalIgnoreCase)
            ? ""
            : $" USING {index.Method}";
        var cols = string.Join(", ",
            index.Columns.Select(c =>
                $"{(c.IsExpression ? c.Name : $"\"{c.Name}\"")}{(c.IsDescending ? " DESC" : "")}"));

        var sb = new StringBuilder();
        sb.Append($"CREATE {unique}INDEX IF NOT EXISTS \"{index.Name}\" ON \"{tableName}\"{method} ({cols})");
        if (index.IncludeColumns.Count > 0)
            sb.Append($" INCLUDE ({string.Join(", ", index.IncludeColumns.Select(c => $"\"{c}\""))})");
        if (!string.IsNullOrWhiteSpace(index.Predicate))
            sb.Append($" WHERE {index.Predicate}");
        sb.Append(';');
        return sb.ToString();
    }

    private static readonly Regex ConcurrentlyPattern = new(
        @"^\s*CREATE\s+(?:UNIQUE\s+)?INDEX\s+(?<kw>CONCURRENTLY)\b\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 移除 <c>CREATE [UNIQUE] INDEX CONCURRENTLY</c> 的 CONCURRENTLY：部署群組在交易內執行，
    /// 而 CONCURRENTLY 不能在交易區塊中使用（cannot run inside a transaction block）。
    /// 以遮蔽註解／字串後的文字比對，避免誤改註解或字串常值中的同名文字。
    /// </summary>
    private static string RemoveConcurrently(string definition)
    {
        var m = ConcurrentlyPattern.Match(SqlTextScanner.Mask(definition));
        if (!m.Success)
            return definition;

        var kw = m.Groups["kw"];
        var end = m.Index + m.Length; // 一併移除關鍵字後的空白
        return definition[..kw.Index] + definition[end..];
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

    private static string ToSqlCommentLiteral(string? comment)
    {
        if (comment == null)
            return "NULL";

        return $"'{comment.Replace("'", "''")}'";
    }
}
