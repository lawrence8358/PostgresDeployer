namespace PostgresDeployer.Core.Models;

/// <summary>
/// 索引定義
/// </summary>
public class IndexDefinition
{
    /// <summary>索引名稱</summary>
    public string Name { get; set; } = "";

    /// <summary>索引包含的欄位清單（有序）</summary>
    public List<IndexColumn> Columns { get; set; } = [];

    /// <summary>是否為唯一索引 (UNIQUE INDEX)</summary>
    public bool IsUnique { get; set; } = false;

    /// <summary>索引存取方法（btree、gin、gist、hash…），小寫；未指定時為 btree</summary>
    public string Method { get; set; } = "btree";

    /// <summary>INCLUDE 子句中的非鍵欄位（覆蓋索引），無則為空清單</summary>
    public List<string> IncludeColumns { get; set; } = [];

    /// <summary>
    /// 部分索引（partial index）的 WHERE 條件（不含 WHERE 關鍵字）。null 表示非部分索引。
    /// </summary>
    public string? Predicate { get; set; }

    /// <summary>
    /// SQL 定義檔中的原始 CREATE INDEX 陳述式（不含結尾分號）。
    /// 僅在從 SQL 檔案解析時有值；部署時優先原樣執行，以保留 WHERE / INCLUDE / WITH 等所有子句。
    /// </summary>
    public string? Definition { get; set; }
}

/// <summary>
/// 索引中的欄位定義
/// </summary>
public class IndexColumn
{
    /// <summary>欄位名稱；若 <see cref="IsExpression"/> 為 true，則為運算式原文（如 lower("Email")）</summary>
    public string Name { get; set; } = "";

    /// <summary>是否為降序排列 (DESC)</summary>
    public bool IsDescending { get; set; } = false;

    /// <summary>是否為運算式（非單純欄位名稱，含 opclass / COLLATE 的項目也視為運算式）</summary>
    public bool IsExpression { get; set; } = false;
}
