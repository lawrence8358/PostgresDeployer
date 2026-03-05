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
}
