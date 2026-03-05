namespace PostgresDeployer.Core.Services;

using PostgresDeployer.Core.Models;

/// <summary>
/// 部署後 SQL 紀錄寫入器。
/// 將 DeployPlan 中所有群組的 SQL 語句依執行順序合併，
/// 輸出至 &lt;outputRootDir&gt;/RunScripts/yyyyMMddHHmmss.sql。
/// </summary>
public class RunScriptWriter
{
    /// <summary>
    /// 將 DeployPlan 的所有 SQL 語句合併輸出為 RunScript 檔案。
    /// </summary>
    /// <param name="plan">部署計畫（含所有群組與語句）</param>
    /// <param name="hostInfo">連線描述（用於檔頭，如 "localhost:5432/MyDb"）</param>
    /// <param name="outputRootDir">RunScripts 資料夾的上層目錄（通常為執行檔所在目錄）</param>
    /// <param name="timestamp">產生時間（null 時使用 DateTime.Now）</param>
    /// <returns>產生的檔案完整路徑</returns>
    public string Write(DeployPlan plan, string hostInfo, string outputRootDir, DateTime? timestamp = null)
    {
        var ts = timestamp ?? DateTime.Now;
        var fileName = ts.ToString("yyyyMMddHHmmss") + ".sql";
        var dir = Path.Combine(outputRootDir, "RunScripts");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, fileName);

        using var writer = new StreamWriter(filePath, append: false, System.Text.Encoding.UTF8);

        // 檔頭
        writer.WriteLine("-- PostgresDeployer RunScript");
        writer.WriteLine($"-- Generated: {ts:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"-- Host:      {hostInfo}");
        writer.WriteLine("-- ============================================================");

        foreach (var group in plan.Groups)
        {
            if (group.Statements.Count == 0) continue;

            writer.WriteLine();
            writer.WriteLine($"-- [Group: {group.Name}]");

            foreach (var sql in group.Statements)
            {
                var trimmed = sql.TrimEnd();
                writer.WriteLine(trimmed);
                // 確保每條語句結尾有分號
                if (!trimmed.EndsWith(';'))
                    writer.WriteLine(";");
                writer.WriteLine();
            }
        }

        return filePath;
    }
}
