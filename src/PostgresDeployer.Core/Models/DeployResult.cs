namespace PostgresDeployer.Core.Models;

/// <summary>
/// 單一部署群組的執行結果
/// </summary>
public class DeployResult
{
    /// <summary>是否執行成功</summary>
    public bool Success { get; set; }

    /// <summary>群組名稱</summary>
    public string GroupName { get; set; } = "";

    /// <summary>成功執行的 SQL 語句數量</summary>
    public int StatementsExecuted { get; set; }

    /// <summary>錯誤訊息（Success=false 時有值）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>導致失敗的 SQL 語句（Success=false 時有值）</summary>
    public string? FailedSql { get; set; }

    /// <summary>執行耗時</summary>
    public TimeSpan Duration { get; set; }
}
