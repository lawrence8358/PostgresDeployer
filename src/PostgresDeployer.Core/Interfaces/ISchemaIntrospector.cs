namespace PostgresDeployer.Core.Interfaces;

using PostgresDeployer.Core.Models;

/// <summary>
/// 資料庫 Schema 內省介面。
/// 負責查詢 PostgreSQL 系統表，取得資料庫現有的 Schema 結構。
/// </summary>
public interface ISchemaIntrospector
{
    /// <summary>取得 public schema 下所有 BASE TABLE 名稱</summary>
    Task<List<string>> GetTableNamesAsync(CancellationToken ct = default);

    /// <summary>取得指定表的完整 Schema（欄位、主鍵、索引）</summary>
    Task<TableSchema> GetTableSchemaAsync(string tableName, CancellationToken ct = default);

    /// <summary>取得所有表的 Schema，以表名為 key 的 Dictionary</summary>
    Task<Dictionary<string, TableSchema>> GetAllTableSchemasAsync(CancellationToken ct = default);

    /// <summary>取得 public schema 下所有 View 名稱</summary>
    Task<List<string>> GetViewNamesAsync(CancellationToken ct = default);

    /// <summary>取得 public schema 下所有 Function 名稱</summary>
    Task<List<string>> GetFunctionNamesAsync(CancellationToken ct = default);

    /// <summary>取得 public schema 下所有 Sequence 名稱</summary>
    Task<List<string>> GetSequenceNamesAsync(CancellationToken ct = default);

    /// <summary>測試資料庫連線是否正常</summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
