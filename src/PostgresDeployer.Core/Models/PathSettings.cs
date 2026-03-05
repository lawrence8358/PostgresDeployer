namespace PostgresDeployer.Core.Models;

/// <summary>
/// SQL 檔案路徑設定。所有子目錄路徑相對於 BasePath，且支援子資料夾（遞迴掃描）。
/// </summary>
public class PathSettings
{
    /// <summary>SQL 檔案根目錄（絕對或相對路徑）</summary>
    public string BasePath { get; set; } = ".";

    /// <summary>Schema DDL 子目錄（含 Table、View、Function、Sequence、Procedure）</summary>
    public string Schema { get; set; } = "Schema";

    /// <summary>初始資料子目錄（INSERT / Seed Data）</summary>
    public string InitData { get; set; } = "InitData";

    /// <summary>
    /// 取得指定子目錄的完整路徑。
    /// 若子目錄為絕對路徑則直接回傳，否則與 BasePath 結合。
    /// </summary>
    /// <param name="subDir">子目錄路徑（如 Schema、InitData 屬性值）</param>
    public string GetFullPath(string subDir)
        => Path.IsPathRooted(subDir)
            ? subDir
            : Path.GetFullPath(Path.Combine(BasePath, subDir));
}
