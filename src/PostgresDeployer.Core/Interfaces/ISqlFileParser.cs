namespace PostgresDeployer.Core.Interfaces;

using PostgresDeployer.Core.Models;

/// <summary>
/// SQL 檔案解析器介面。
/// 負責從 SQL 定義檔案中擷取結構化 Schema 資訊。
/// </summary>
public interface ISqlFileParser
{
    /// <summary>
    /// 解析單一 Table SQL 檔案，回傳 TableSchema。
    /// </summary>
    TableSchema ParseTableFile(string filePath);

    /// <summary>
    /// 從 SQL 字串直接解析表結構，不依賴檔案系統。
    /// </summary>
    TableSchema ParseTableSql(string sql);

    /// <summary>
    /// 解析指定目錄下所有 *.sql 檔案，回傳 TableSchema 清單。
    /// </summary>
    List<TableSchema> ParseTableDirectory(string directoryPath);

    /// <summary>
    /// 讀取指定目錄下所有 *.sql 檔案的原始內容（遞迴掃描子資料夾）。
    /// 回傳的 FileName 為相對於 directoryPath 的相對路徑，依相對路徑字母序排序。
    /// 目錄不存在時回傳空清單。
    /// </summary>
    List<(string FileName, string Content)> ReadSqlFiles(string directoryPath);
}
