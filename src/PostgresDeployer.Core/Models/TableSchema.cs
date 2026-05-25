namespace PostgresDeployer.Core.Models;

/// <summary>
/// 資料表完整 Schema 定義。
/// 由 SqlFileParser（從 SQL 檔案解析）或 SchemaIntrospector（從 DB 內省）產生。
/// </summary>
public class TableSchema
{
    /// <summary>資料表名稱（不含引號），如 "Base_Auth_User"</summary>
    public string TableName { get; set; } = "";

    /// <summary>CREATE TABLE 是否使用了 IF NOT EXISTS 語法</summary>
    public bool IfNotExists { get; set; } = false;

    /// <summary>欄位定義清單（依宣告順序）</summary>
    public List<ColumnDefinition> Columns { get; set; } = [];

    /// <summary>主鍵約束定義。若無主鍵則為 null。</summary>
    public PrimaryKeyDefinition? PrimaryKey { get; set; }

    /// <summary>索引定義清單（不含主鍵索引）</summary>
    public List<IndexDefinition> Indexes { get; set; } = [];

    /// <summary>外鍵約束定義清單</summary>
    public List<ForeignKeyDefinition> ForeignKeys { get; set; } = [];

    /// <summary>
    /// 原始 SQL 檔案完整內容。
    /// 用於 CREATE TABLE 時直接執行原始 SQL（新建表不需重新產生）。
    /// 僅在從 SQL 檔案解析時有值，DB 內省時為 null。
    /// </summary>
    public string? RawSql { get; set; }
}
