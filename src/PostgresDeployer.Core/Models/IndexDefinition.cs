namespace PostgresDeployer.Core.Models;

/// <summary>
/// 索引定義（不含主鍵索引）
/// </summary>
public class IndexDefinition
{
    /// <summary>索引名稱</summary>
    public string Name { get; set; } = "";

    /// <summary>索引包含的欄位清單（有序）</summary>
    public List<IndexColumn> Columns { get; set; } = [];

    /// <summary>是否為唯一索引 (UNIQUE INDEX)</summary>
    public bool IsUnique { get; set; } = false;
}

/// <summary>
/// 索引中的欄位定義
/// </summary>
public class IndexColumn
{
    /// <summary>欄位名稱</summary>
    public string Name { get; set; } = "";

    /// <summary>是否為降序排列 (DESC)</summary>
    public bool IsDescending { get; set; } = false;
}
