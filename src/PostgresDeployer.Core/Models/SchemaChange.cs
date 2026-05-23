namespace PostgresDeployer.Core.Models;

/// <summary>
/// 單一 Schema 差異變更項目。所有變更類型皆會自動執行。
/// </summary>
public class SchemaChange
{
    /// <summary>變更類型</summary>
    public ChangeType Type { get; set; }

    /// <summary>影響的實體名稱（資料表名稱或 View 名稱，依 <see cref="Type"/> 決定）</summary>
    public string EntityName { get; set; } = "";

    /// <summary>影響的欄位名稱（若適用）</summary>
    public string? ColumnName { get; set; }

    /// <summary>變更的人類可讀描述（繁體中文）</summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 要執行的 SQL 語句。
    /// 對於 CreateTable 類型，為原始 SQL 檔案內容。
    /// </summary>
    public string? Sql { get; set; }

    /// <summary>
    /// 注意事項提示訊息。
    /// 用於型別縮窄、NOT NULL 無 DEFAULT、刪除欄位、重建主鍵等潛在風險操作。
    /// 不影響執行，僅供 CLI/WPF 顯示提醒。
    /// </summary>
    public string? CautionMessage { get; set; }
}
