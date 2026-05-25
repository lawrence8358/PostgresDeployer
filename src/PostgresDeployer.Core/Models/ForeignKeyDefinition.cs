namespace PostgresDeployer.Core.Models;

/// <summary>
/// 外鍵約束定義。
/// 由 SqlFileParser（從 SQL 檔案解析）或 SchemaIntrospector（從 DB 內省）產生。
/// </summary>
public class ForeignKeyDefinition
{
    /// <summary>約束名稱，如 "FK_Order_UserId"</summary>
    public string ConstraintName { get; set; } = "";

    /// <summary>外鍵欄位清單（本表端，依宣告順序）</summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>被參照的資料表名稱（不含引號）</summary>
    public string ReferencedTable { get; set; } = "";

    /// <summary>被參照的欄位清單（依序，對應 Columns）</summary>
    public List<string> ReferencedColumns { get; set; } = [];

    /// <summary>ON DELETE 動作，如 "CASCADE"、"SET NULL"、"NO ACTION"（預設）</summary>
    public string OnDelete { get; set; } = "NO ACTION";

    /// <summary>ON UPDATE 動作，如 "CASCADE"、"SET NULL"、"NO ACTION"（預設）</summary>
    public string OnUpdate { get; set; } = "NO ACTION";
}
