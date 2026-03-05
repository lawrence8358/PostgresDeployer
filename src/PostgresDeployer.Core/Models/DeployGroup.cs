namespace PostgresDeployer.Core.Models;

/// <summary>
/// 部署群組。同一群組的 SQL 語句在同一個交易中執行。
/// </summary>
public class DeployGroup
{
    /// <summary>群組名稱（顯示用），如 "Extensions"、"新建資料表"、"Views"</summary>
    public string Name { get; set; } = "";

    /// <summary>要執行的 SQL 語句清單（有序）</summary>
    public List<string> Statements { get; set; } = [];

    /// <summary>此群組包含的 Schema 變更項目（用於 UI 顯示）</summary>
    public List<SchemaChange> Changes { get; set; } = [];

    /// <summary>與 Statements 平行對應的顯示標籤（如檔案名稱），供 UI 顯示用</summary>
    public List<string> StatementLabels { get; set; } = [];
}
