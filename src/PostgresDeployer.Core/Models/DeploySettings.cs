using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostgresDeployer.Core.Models;

/// <summary>
/// 部署設定（CLI 與 WPF 共用）。對應 JSON 設定檔格式。
/// </summary>
public class DeploySettings
{
    /// <summary>資料庫連線設定</summary>
    public ConnectionSettings Connection { get; set; } = new();

    /// <summary>SQL 檔案路徑設定</summary>
    public PathSettings Paths { get; set; } = new();

    /// <summary>需要建立的 PostgreSQL Extensions</summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>部署行為選項</summary>
    public DeployOptions Options { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>從 JSON 檔案載入設定</summary>
    public static DeploySettings LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<DeploySettings>(json, JsonOptions)
            ?? throw new InvalidOperationException($"無法從檔案反序列化設定: {filePath}");
    }

    /// <summary>儲存設定到 JSON 檔案</summary>
    public void SaveToFile(string filePath)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, json);
    }

    /// <summary>從 JSON 字串反序列化</summary>
    public static DeploySettings FromJson(string json)
        => JsonSerializer.Deserialize<DeploySettings>(json, JsonOptions)
            ?? throw new InvalidOperationException("無法從 JSON 字串反序列化設定");

    /// <summary>序列化為 JSON 字串</summary>
    public string ToJson()
        => JsonSerializer.Serialize(this, JsonOptions);
}
