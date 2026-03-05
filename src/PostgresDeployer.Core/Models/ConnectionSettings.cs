namespace PostgresDeployer.Core.Models;

using Npgsql;

/// <summary>
/// PostgreSQL 連線設定
/// </summary>
public class ConnectionSettings
{
    /// <summary>PostgreSQL 主機位址</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>PostgreSQL 連接埠</summary>
    public int Port { get; set; } = 5432;

    /// <summary>資料庫名稱</summary>
    public string Database { get; set; } = "";

    /// <summary>使用者名稱</summary>
    public string Username { get; set; } = "";

    /// <summary>密碼</summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// 產生 Npgsql 連線字串
    /// </summary>
    public string ToConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password
        };
        return builder.ConnectionString;
    }
}
