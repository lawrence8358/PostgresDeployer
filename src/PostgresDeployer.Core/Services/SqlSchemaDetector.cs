namespace PostgresDeployer.Core.Services;

using System.Text.RegularExpressions;
using PostgresDeployer.Core.Models;

/// <summary>
/// 從 SQL 檔案內容自動偵測 Schema 類型。
/// </summary>
public static class SqlSchemaDetector
{
    private static readonly Regex TableRx = new(
        @"^\s*CREATE\s+TABLE\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex ViewRx = new(
        @"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?VIEW\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex SequenceRx = new(
        @"^\s*CREATE\s+(?:IF\s+NOT\s+EXISTS\s+)?SEQUENCE\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex FunctionRx = new(
        @"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?FUNCTION\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex ProcedureRx = new(
        @"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?PROCEDURE\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>
    /// 依 SQL 內容偵測 Schema 類型。無法識別時回傳 <see cref="SchemaFileType.Unknown"/>。
    /// </summary>
    public static SchemaFileType Detect(string sqlContent)
    {
        if (TableRx.IsMatch(sqlContent))     return SchemaFileType.Table;
        if (ViewRx.IsMatch(sqlContent))      return SchemaFileType.View;
        if (SequenceRx.IsMatch(sqlContent))  return SchemaFileType.Sequence;
        if (FunctionRx.IsMatch(sqlContent))  return SchemaFileType.Function;
        if (ProcedureRx.IsMatch(sqlContent)) return SchemaFileType.Procedure;
        return SchemaFileType.Unknown;
    }
}
