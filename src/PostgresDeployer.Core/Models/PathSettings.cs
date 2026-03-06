namespace PostgresDeployer.Core.Models;

/// <summary>
/// SQL 檔案路徑設定。Schema 與 InitData 均為使用者提供的絕對路徑，
/// 由 CLI 參數（--schema / --init-data）、config 檔或 WPF 資料夾選擇器指定。
/// </summary>
public class PathSettings
{
    /// <summary>Schema DDL 目錄的絕對路徑（含 Table、View、Function、Sequence、Procedure）</summary>
    public string Schema { get; set; } = "";

    /// <summary>初始資料目錄的絕對路徑（INSERT / Seed Data）</summary>
    public string InitData { get; set; } = "";
}
