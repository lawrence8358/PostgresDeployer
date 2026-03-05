namespace PostgresDeployer.Core.Services;

using System.Text.RegularExpressions;
using Npgsql;
using PostgresDeployer.Core.Helpers;
using PostgresDeployer.Core.Interfaces;
using PostgresDeployer.Core.Models;

/// <summary>
/// PostgreSQL 資料庫 Schema 內省器。
/// 透過 information_schema 和 pg_catalog 查詢資料庫現有的 Schema 結構。
/// </summary>
public class SchemaIntrospector : ISchemaIntrospector
{
    private readonly string _connectionString;

    private static readonly Regex IndexColumnsRegex = new(
        @"\(([^)]+)\)$", RegexOptions.Compiled);

    public SchemaIntrospector(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT current_database(), current_user, version()", conn);
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> GetTableNamesAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var names = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    public async Task<TableSchema> GetTableSchemaAsync(string tableName, CancellationToken ct = default)
    {
        var schema = new TableSchema { TableName = tableName };

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // ═══ 查詢欄位 ═══
        const string columnSql = """
            SELECT
                column_name,
                data_type,
                udt_name,
                character_maximum_length,
                numeric_precision,
                numeric_scale,
                is_nullable,
                column_default,
                is_identity
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @tableName
            ORDER BY ordinal_position
            """;

        await using (var cmd = new NpgsqlCommand(columnSql, conn))
        {
            cmd.Parameters.AddWithValue("@tableName", tableName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var dataType = reader.GetString(1);
                var udtName = reader.IsDBNull(2) ? null : reader.GetString(2);
                var charMaxLen = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var numPrecision = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var numScale = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var isNullable = reader.GetString(6) == "YES";
                var columnDefault = reader.IsDBNull(7) ? null : reader.GetString(7);
                var isIdentity = reader.GetString(8) == "YES";

                var typeInfo = TypeNormalizer.NormalizeFromDb(
                    dataType, udtName, charMaxLen, numPrecision, numScale);

                var normalizedDefault = DefaultValueNormalizer.NormalizeFromDb(
                    columnDefault, isIdentity);

                schema.Columns.Add(new ColumnDefinition
                {
                    Name = reader.GetString(0),
                    Type = typeInfo.BaseType,
                    RawType = BuildRawType(typeInfo.BaseType, typeInfo.Length, typeInfo.Precision, typeInfo.Scale),
                    Length = typeInfo.Length,
                    Precision = typeInfo.Precision,
                    Scale = typeInfo.Scale,
                    IsNullable = isNullable,
                    HasDefault = normalizedDefault != null,
                    DefaultValue = normalizedDefault,
                    IsIdentity = isIdentity
                });
            }
        }

        // ═══ 查詢主鍵 ═══
        const string pkSql = """
            SELECT
                tc.constraint_name,
                kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.table_schema = 'public'
                AND tc.table_name = @tableName
                AND tc.constraint_type = 'PRIMARY KEY'
            ORDER BY kcu.ordinal_position
            """;

        await using (var cmd = new NpgsqlCommand(pkSql, conn))
        {
            cmd.Parameters.AddWithValue("@tableName", tableName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            string? constraintName = null;
            var pkColumns = new List<string>();

            while (await reader.ReadAsync(ct))
            {
                constraintName ??= reader.GetString(0);
                pkColumns.Add(reader.GetString(1));
            }

            if (pkColumns.Count > 0)
            {
                schema.PrimaryKey = new PrimaryKeyDefinition
                {
                    ConstraintName = constraintName,
                    Columns = pkColumns
                };
            }
        }

        // ═══ 查詢索引（排除主鍵索引）═══
        const string indexSql = """
            SELECT
                i.relname AS index_name,
                ix.indisunique AS is_unique,
                pg_get_indexdef(ix.indexrelid) AS index_def
            FROM pg_class t
            JOIN pg_index ix ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'public'
                AND t.relname = @tableName
                AND ix.indisprimary = false
            ORDER BY i.relname
            """;

        await using (var cmd = new NpgsqlCommand(indexSql, conn))
        {
            cmd.Parameters.AddWithValue("@tableName", tableName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var indexName = reader.GetString(0);
                var isUnique = reader.GetBoolean(1);
                var indexDef = reader.GetString(2);

                var columns = ParseIndexDefColumns(indexDef);

                schema.Indexes.Add(new IndexDefinition
                {
                    Name = indexName,
                    IsUnique = isUnique,
                    Columns = columns
                });
            }
        }

        return schema;
    }

    public async Task<Dictionary<string, TableSchema>> GetAllTableSchemasAsync(CancellationToken ct = default)
    {
        var names = await GetTableNamesAsync(ct);
        var result = new Dictionary<string, TableSchema>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            result[name] = await GetTableSchemaAsync(name, ct);
        }
        return result;
    }

    public async Task<List<string>> GetViewNamesAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT table_name
            FROM information_schema.views
            WHERE table_schema = 'public'
            ORDER BY table_name
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var names = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    public async Task<List<string>> GetFunctionNamesAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT routine_name
            FROM information_schema.routines
            WHERE routine_schema = 'public'
              AND routine_type IN ('FUNCTION', 'PROCEDURE')
            ORDER BY routine_name
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var names = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    public async Task<List<string>> GetSequenceNamesAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT sequencename
            FROM pg_catalog.pg_sequences
            WHERE schemaname = 'public'
            ORDER BY sequencename
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var names = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    /// <summary>
    /// 從 pg_get_indexdef() 結果解析索引欄位清單。
    /// </summary>
    private static List<IndexColumn> ParseIndexDefColumns(string indexDef)
    {
        var match = IndexColumnsRegex.Match(indexDef);
        if (!match.Success)
            return [];

        var columnsText = match.Groups[1].Value;
        return columnsText.Split(',')
            .Select(c =>
            {
                var trimmed = c.Trim();
                var isDesc = trimmed.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase);
                var name = trimmed
                    .Replace(" DESC", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ASC", "", StringComparison.OrdinalIgnoreCase)
                    .Trim()
                    .Trim('"');

                return new IndexColumn { Name = name, IsDescending = isDesc };
            })
            .Where(c => c.Name.Length > 0)
            .ToList();
    }

    /// <summary>
    /// 從正規化型別資訊重建 SQL 格式的 RawType 字串。
    /// </summary>
    private static string BuildRawType(string baseType, int? length, int? precision, int? scale)
    {
        if (length.HasValue && (baseType is "VARCHAR" or "CHAR"))
            return $"{baseType}({length.Value})";

        if (precision.HasValue && scale.HasValue && baseType is "DECIMAL")
            return $"{baseType}({precision.Value},{scale.Value})";

        if (precision.HasValue && baseType is "DECIMAL")
            return $"{baseType}({precision.Value})";

        return baseType;
    }
}
