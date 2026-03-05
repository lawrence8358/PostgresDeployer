namespace PostgresDeployer.Core.Helpers;

using System.Text.RegularExpressions;

/// <summary>
/// PostgreSQL 型別正規化工具。
/// 將 SQL 檔案中的型別和 DB information_schema 的型別統一正規化為相同格式。
/// </summary>
public static class TypeNormalizer
{
    private static readonly Dictionary<string, string> SqlTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NCHAR"] = "CHAR",
        ["INTEGER"] = "INT",
        ["NUMERIC"] = "DECIMAL",
    };

    private static readonly Dictionary<string, string> DbTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["character varying"] = "VARCHAR",
        ["character"] = "CHAR",
        ["integer"] = "INT",
        ["smallint"] = "SMALLINT",
        ["bigint"] = "BIGINT",
        ["boolean"] = "BOOLEAN",
        ["uuid"] = "UUID",
        ["timestamp with time zone"] = "TIMESTAMPTZ",
        ["timestamp without time zone"] = "TIMESTAMP",
        ["date"] = "DATE",
        ["numeric"] = "DECIMAL",
        ["text"] = "TEXT",
        ["bytea"] = "BYTEA",
    };

    private static readonly HashSet<string> CharTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "VARCHAR", "CHAR"
    };

    private static readonly HashSet<string> IntTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "SMALLINT", "BIGINT"
    };

    private static readonly Regex TypePattern = new(@"^(\w+)\s*(?:\(([^)]+)\))?$", RegexOptions.Compiled);

    /// <summary>
    /// 從 SQL 檔案的型別文字擷取正規化資訊。
    /// </summary>
    public static (string BaseType, int? Length, int? Precision, int? Scale) ParseSqlType(string rawType)
    {
        var trimmed = rawType.Trim().ToUpperInvariant();
        var match = TypePattern.Match(trimmed);
        if (!match.Success)
            return (trimmed, null, null, null);

        var typeName = match.Groups[1].Value;
        var args = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;

        // Normalize type name
        if (SqlTypeMap.TryGetValue(typeName, out var mapped))
            typeName = mapped;

        int? length = null, precision = null, scale = null;

        if (args != null)
        {
            var parts = args.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 1 && int.TryParse(parts[0], out var val))
            {
                if (CharTypes.Contains(typeName))
                    length = val;
                else
                    precision = val;
            }
            else if (parts.Length == 2
                     && int.TryParse(parts[0], out var p)
                     && int.TryParse(parts[1], out var s))
            {
                precision = p;
                scale = s;
            }
        }

        return (typeName, length, precision, scale);
    }

    /// <summary>
    /// 從 DB information_schema 的值正規化為統一格式。
    /// </summary>
    public static (string BaseType, int? Length, int? Precision, int? Scale)
        NormalizeFromDb(string dataType, string? udtName, int? charMaxLen, int? numPrecision, int? numScale)
    {
        string baseType;
        if (DbTypeMap.TryGetValue(dataType, out var mapped))
            baseType = mapped;
        else if (string.Equals(dataType, "USER-DEFINED", StringComparison.OrdinalIgnoreCase) && udtName != null)
            baseType = udtName.ToUpperInvariant();
        else
            baseType = dataType.ToUpperInvariant();

        int? length = null, precision = null, scale = null;

        if (CharTypes.Contains(baseType))
        {
            length = charMaxLen;
        }
        else if (string.Equals(baseType, "DECIMAL", StringComparison.OrdinalIgnoreCase))
        {
            precision = numPrecision;
            scale = numScale;
        }
        // INT/SMALLINT/BIGINT: ignore numPrecision/numScale (PG internal values like 32/0)

        return (baseType, length, precision, scale);
    }

    /// <summary>
    /// 比較兩個正規化後的型別是否相同。
    /// </summary>
    public static bool TypesMatch(
        (string BaseType, int? Length, int? Precision, int? Scale) a,
        (string BaseType, int? Length, int? Precision, int? Scale) b)
    {
        if (!string.Equals(a.BaseType, b.BaseType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (CharTypes.Contains(a.BaseType))
            return a.Length == b.Length;

        if (string.Equals(a.BaseType, "DECIMAL", StringComparison.OrdinalIgnoreCase))
            return a.Precision == b.Precision && (a.Scale ?? 0) == (b.Scale ?? 0);

        return true;
    }

    /// <summary>
    /// 判斷型別變更是否為縮窄（可能導致資料截斷）。
    /// </summary>
    public static bool IsTypeNarrowing(
        (string BaseType, int? Length, int? Precision, int? Scale) desired,
        (string BaseType, int? Length, int? Precision, int? Scale) actual)
    {
        if (string.Equals(desired.BaseType, actual.BaseType, StringComparison.OrdinalIgnoreCase))
        {
            if (CharTypes.Contains(desired.BaseType))
                return desired.Length < actual.Length;

            if (string.Equals(desired.BaseType, "DECIMAL", StringComparison.OrdinalIgnoreCase))
                return desired.Precision < actual.Precision
                    || (desired.Precision == actual.Precision && (desired.Scale ?? 0) < (actual.Scale ?? 0));
        }

        // Different base types: check storage size narrowing
        var desiredSize = GetTypeStorageRank(desired.BaseType);
        var actualSize = GetTypeStorageRank(actual.BaseType);
        if (desiredSize > 0 && actualSize > 0)
            return desiredSize < actualSize;

        return false;
    }

    private static int GetTypeStorageRank(string baseType)
    {
        return baseType.ToUpperInvariant() switch
        {
            "SMALLINT" => 1,
            "INT" => 2,
            "BIGINT" => 3,
            _ => 0
        };
    }
}
