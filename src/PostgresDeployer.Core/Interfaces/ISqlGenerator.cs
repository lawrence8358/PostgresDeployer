namespace PostgresDeployer.Core.Interfaces;

using PostgresDeployer.Core.Models;

/// <summary>
/// SQL 語句產生器介面。
/// 根據 Schema 差異產生對應的 ALTER/CREATE SQL 語句。
/// </summary>
public interface ISqlGenerator
{
    /// <summary>產生 ALTER TABLE ADD COLUMN 語句</summary>
    string GenerateAddColumn(string tableName, ColumnDefinition column);

    /// <summary>產生 ALTER TABLE ALTER COLUMN TYPE 語句（含 USING 子句）</summary>
    string GenerateAlterColumnType(string tableName, string columnName, string newRawType);

    /// <summary>產生 ALTER TABLE ALTER COLUMN SET/DROP NOT NULL 語句</summary>
    string GenerateAlterColumnNullable(string tableName, string columnName, bool nullable);

    /// <summary>產生 ALTER TABLE ALTER COLUMN SET/DROP DEFAULT 語句</summary>
    string GenerateAlterColumnDefault(string tableName, string columnName, string? defaultValue);

    /// <summary>產生 COMMENT ON TABLE 語句</summary>
    string GenerateAlterTableComment(string tableName, string? comment);

    /// <summary>產生 COMMENT ON COLUMN 語句</summary>
    string GenerateAlterColumnComment(string tableName, string columnName, string? comment);

    /// <summary>產生 CREATE INDEX 語句</summary>
    string GenerateCreateIndex(string tableName, IndexDefinition index);

    /// <summary>產生 DROP INDEX 語句</summary>
    string GenerateDropIndex(string indexName);

    /// <summary>產生 ALTER TABLE DROP COLUMN 語句</summary>
    string GenerateDropColumn(string tableName, string columnName);

    /// <summary>產生 ALTER TABLE DROP CONSTRAINT（主鍵）語句</summary>
    string GenerateDropPrimaryKey(string tableName, string constraintName);

    /// <summary>產生 ALTER TABLE ADD CONSTRAINT PRIMARY KEY 語句</summary>
    string GenerateCreatePrimaryKey(string tableName, PrimaryKeyDefinition pk);

    /// <summary>產生 ALTER TABLE ADD CONSTRAINT FOREIGN KEY 語句</summary>
    string GenerateAddForeignKey(string tableName, ForeignKeyDefinition fk);

    /// <summary>產生 ALTER TABLE DROP CONSTRAINT（外鍵）語句</summary>
    string GenerateDropForeignKey(string tableName, string constraintName);
}
