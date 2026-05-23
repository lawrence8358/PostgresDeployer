namespace PostgresDeployer.Core.Interfaces;

using PostgresDeployer.Core.Models;

/// <summary>
/// Schema 差異比對引擎介面。
/// 比較「期望狀態」（SQL 檔案定義）與「實際狀態」（DB 現有 Schema），
/// 產生差異變更清單。
/// </summary>
public interface ISchemaDiffer
{
    /// <summary>
    /// 計算 Schema 差異。
    /// </summary>
    /// <param name="desired">期望的表結構清單（從 SQL 檔案解析）</param>
    /// <param name="actual">資料庫現有的表結構（以表名為 key）</param>
    /// <returns>差異變更清單，所有變更皆會自動執行</returns>
    List<SchemaChange> ComputeChanges(
        List<TableSchema> desired,
        Dictionary<string, TableSchema> actual);

    /// <summary>
    /// 計算 View 差異（依欄位名稱比對）。
    /// </summary>
    /// <param name="desiredViews">期望的 View 清單（ViewName, SQL 內容）</param>
    /// <param name="existingViewColumns">資料庫現有 View 的欄位名稱（依順序），以 View 名稱為 key</param>
    /// <returns>差異變更清單（CreateView 或 ReplaceView）</returns>
    List<SchemaChange> ComputeViewChanges(
        List<(string ViewName, string SqlContent)> desiredViews,
        Dictionary<string, List<string>> existingViewColumns);
}
