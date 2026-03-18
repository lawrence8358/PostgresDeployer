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
    /// 完整連線字串（可選）。
    /// 設定後呼叫 <see cref="ApplyConnectionString"/> 可將各欄位解析並填入 Host/Port/Database/Username/Password。
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 將連線字串解析，並將各欄位填入 Host/Port/Database/Username/Password。
    /// </summary>
    public void ApplyConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Host)) Host = builder.Host;
        if (builder.Port > 0) Port = builder.Port;
        if (!string.IsNullOrEmpty(builder.Database)) Database = builder.Database;
        if (!string.IsNullOrEmpty(builder.Username)) Username = builder.Username;
        if (!string.IsNullOrEmpty(builder.Password)) Password = builder.Password;
    }

    /// <summary>
    /// 產生 Npgsql 連線字串（目標資料庫）
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

    /// <summary>
    /// 產生連線至伺服器預設資料庫（postgres）的連線字串，
    /// 用於在目標資料庫不存在時進行伺服器層級的連線測試或建立資料庫。
    /// </summary>
    public string ToServerConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = "postgres",
            Username = Username,
            Password = Password
        };
        return builder.ConnectionString;
    }
}
