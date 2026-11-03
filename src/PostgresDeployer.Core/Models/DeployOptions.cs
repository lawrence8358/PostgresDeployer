namespace PostgresDeployer.Core.Models;

/// <summary>
/// 部署行為選項
/// </summary>
public class DeployOptions
{
    /// <summary>是否執行 Seed Data（Scripts 目錄下的 MERGE 語句）</summary>
    public bool ExecuteSeedData { get; set; } = true;

    /// <summary>發生錯誤時是否停止後續群組的執行</summary>
    public bool StopOnError { get; set; } = true;

    /// <summary>
    /// 若目標資料庫不存在，是否自動建立。
    /// 啟用時：deploy/diff 執行前會先確認資料庫存在；
    /// test-connection 會改為測試伺服器連線（不指定資料庫）。
    /// </summary>
    public bool CreateDatabaseIfNotExists { get; set; } = false;

    /// <summary>
    /// 是否允許 Pre-Drop 的 <c>DROP VIEW ... CASCADE</c> 連帶刪除「不在 schema 目錄中」的物件（View / Materialized View）。
    /// 預設 false：偵測到此情況時部署會被阻擋（<see cref="DeployPlan.IsBlocked"/>），不執行任何群組；
    /// 設為 true 時降級為注意事項（Caution），照常部署，被連帶刪除的物件不會重建。
    /// </summary>
    public bool AllowCascadeDrop { get; set; } = false;
}
