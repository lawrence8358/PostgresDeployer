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
}
