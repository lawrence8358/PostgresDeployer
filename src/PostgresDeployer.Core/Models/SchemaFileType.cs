namespace PostgresDeployer.Core.Models;

/// <summary>
/// SQL 檔案的 Schema 類型，由 <see cref="PostgresDeployer.Core.Services.SqlSchemaDetector"/> 從 SQL 內容自動偵測。
/// </summary>
public enum SchemaFileType
{
    Table,
    View,
    Sequence,
    Function,
    Procedure,
    Unknown
}
