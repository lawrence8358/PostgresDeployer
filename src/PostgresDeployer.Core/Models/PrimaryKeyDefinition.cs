namespace PostgresDeployer.Core.Models;

/// <summary>
/// 主鍵約束定義
/// </summary>
public class PrimaryKeyDefinition
{
    /// <summary>
    /// 約束名稱。例如 "PK_Base_Auth_User"。
    /// 若為 inline PRIMARY KEY（無 CONSTRAINT 關鍵字），則為 null。
    /// </summary>
    public string? ConstraintName { get; set; }

    /// <summary>
    /// 主鍵包含的欄位清單（有序）。
    /// 單一欄位主鍵為 1 個元素，複合主鍵為多個。
    /// </summary>
    public List<string> Columns { get; set; } = [];
}
