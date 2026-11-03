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

    /// <summary>取得 public schema 下所有 View 的名稱與欄位清單（有序）</summary>
    Task<Dictionary<string, List<string>>> GetViewColumnNamesAsync(CancellationToken ct = default);

    /// <summary>取得 public schema 下所有 Function 名稱</summary>
    Task<List<string>> GetFunctionNamesAsync(CancellationToken ct = default);

    /// <summary>取得 public schema 下所有 Sequence 名稱</summary>
    Task<List<string>> GetSequenceNamesAsync(CancellationToken ct = default);

    /// <summary>
    /// 以 PostgreSQL 正規化 SQL 檔中的索引定義（欄位運算式、WHERE 條件），讓差異比對與 pg_catalog 的寫法一致。
    /// 會在暫存表上建立同樣的索引後讀回結果，並在交易結束時回滾（不留下任何物件）。
    /// 無法正規化的索引（如引用尚未存在的欄位）維持原樣。
    /// </summary>
    Task CanonicalizeIndexesAsync(
        IReadOnlyList<(string TableName, IndexDefinition Index)> indexes,
        CancellationToken ct = default);

    /// <summary>
    /// 取得依賴指定 View 的物件（遞迴），即 DROP VIEW ... CASCADE 會一併刪除的 View / Materialized View。
    /// Key 為傳入的 View 名稱，Value 為相依物件的顯示名稱。
    /// </summary>
    Task<Dictionary<string, List<string>>> GetViewDependentsAsync(
        IReadOnlyCollection<string> viewNames,
        CancellationToken ct = default);

    /// <summary>測試資料庫連線是否正常</summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
