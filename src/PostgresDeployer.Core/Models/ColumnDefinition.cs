namespace PostgresDeployer.Core.Models;

/// <summary>
/// 資料表欄位定義。由 SQL 檔案解析或資料庫內省產生。
/// </summary>
public class ColumnDefinition
{
    /// <summary>欄位名稱（不含引號），如 "Id"、"Account"</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 正規化後的基本型別名稱（大寫），如 VARCHAR、INT、UUID、BOOLEAN。
    /// 不含長度/精度參數。用於跨來源比較。
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// 原始 SQL 中的完整型別文字，如 VARCHAR(200)、DECIMAL(18,10)。
    /// 用於產生 ALTER 語句。
    /// </summary>
    public string RawType { get; set; } = "";

    /// <summary>
    /// 字元長度。適用於 VARCHAR(n)、CHAR(n) 等型別。
    /// 例如 VARCHAR(200) → Length = 200。
    /// </summary>
    public int? Length { get; set; }

    /// <summary>
    /// 數值精度。適用於 DECIMAL(p,s)、NUMERIC(p,s)。
    /// 例如 DECIMAL(18,10) → Precision = 18。
    /// </summary>
    public int? Precision { get; set; }

    /// <summary>
    /// 數值小數位。適用於 DECIMAL(p,s)、NUMERIC(p,s)。
    /// 例如 DECIMAL(18,10) → Scale = 10。
    /// </summary>
    public int? Scale { get; set; }

    /// <summary>是否允許 NULL（預設 true）</summary>
    public bool IsNullable { get; set; } = true;

    /// <summary>是否有 DEFAULT 值</summary>
    public bool HasDefault { get; set; } = false;

    /// <summary>
    /// DEFAULT 值文字（正規化後）。
    /// 例如 "FALSE"、"TRUE"、"NOW()"、"0"、"'literal'"。
    /// null 表示無預設值。
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// 是否為 IDENTITY 欄位（GENERATED ALWAYS AS IDENTITY）。
    /// 對應 PostgreSQL 的自動遞增欄位。
    /// </summary>
    public bool IsIdentity { get; set; } = false;

    /// <summary>欄位註解。null 表示未指定或應移除註解。</summary>
    public string? Comment { get; set; }
}
