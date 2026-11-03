namespace PostgresDeployer.Core.Services;

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

    /// <summary>
    /// 索引內省查詢（{0} 為篩選條件）。
    /// 由 pg_index 直接取得每個欄位／運算式、DESC 旗標、INCLUDE 欄位、存取方法與 WHERE 條件，
    /// 不以正規式切割 pg_get_indexdef() 的輸出（partial index 的 WHERE 會被誤當成欄位清單）。
    /// </summary>
    private const string IndexQueryTemplate = """
        SELECT
            i.relname                                   AS index_name,
            ix.indisunique                              AS is_unique,
            am.amname                                   AS method,
            ix.indnkeyatts                              AS key_count,
            ARRAY(SELECT (ix.indoption[k - 1] & 1) = 1
                  FROM generate_series(1, ix.indnkeyatts) AS k
                  ORDER BY k)                           AS desc_flags,
            ARRAY(SELECT pg_get_indexdef(ix.indexrelid, k, true)
                  FROM generate_series(1, ix.indnatts) AS k
                  ORDER BY k)                           AS column_defs,
            pg_get_expr(ix.indpred, ix.indrelid, true)  AS predicate
        FROM pg_index ix
        JOIN pg_class i ON i.oid = ix.indexrelid
        JOIN pg_am am   ON am.oid = i.relam
        WHERE {0}
        ORDER BY i.relname
        """;

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
                c.column_name,
                c.data_type,
                c.udt_name,
                c.character_maximum_length,
                c.numeric_precision,
                c.numeric_scale,
                c.is_nullable,
                c.column_default,
                c.is_identity,
                pgd.description AS column_comment
            FROM information_schema.columns c
            LEFT JOIN pg_catalog.pg_namespace pn
                ON pn.nspname = c.table_schema
            LEFT JOIN pg_catalog.pg_class pc
                ON pc.relname = c.table_name
               AND pc.relnamespace = pn.oid
            LEFT JOIN pg_catalog.pg_attribute pa
                ON pa.attrelid = pc.oid
               AND pa.attname = c.column_name
               AND pa.attnum > 0
               AND NOT pa.attisdropped
            LEFT JOIN pg_catalog.pg_description pgd
                ON pgd.objoid = pc.oid
               AND pgd.objsubid = pa.attnum
            WHERE c.table_schema = 'public'
              AND c.table_name = @tableName
            ORDER BY c.ordinal_position
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
                var columnComment = reader.IsDBNull(9) ? null : reader.GetString(9);

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
                    IsIdentity = isIdentity,
                    Comment = columnComment
                });
            }
        }

        const string tableCommentSql = """
            SELECT
                obj_description(c.oid, 'pg_class') AS table_comment
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relname = @tableName
              AND c.relkind = 'r'
            """;

        await using (var cmd = new NpgsqlCommand(tableCommentSql, conn))
        {
            cmd.Parameters.AddWithValue("@tableName", tableName);
            var result = await cmd.ExecuteScalarAsync(ct);
            schema.Comment = result is DBNull or null ? null : (string)result;
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
        var indexSql = string.Format(IndexQueryTemplate, """
            ix.indrelid = (
                SELECT c.oid FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relname = @tableName AND c.relkind IN ('r', 'p'))
            AND NOT ix.indisprimary
            """);

        await using (var cmd = new NpgsqlCommand(indexSql, conn))
        {
            cmd.Parameters.AddWithValue("@tableName", tableName);
            schema.Indexes.AddRange(await ReadIndexesAsync(cmd, ct));
        }

        // ═══ 查詢外鍵約束 ═══
        const string fkSql = """
            SELECT
                tc.constraint_name,
                kcu.column_name,
                ccu.table_name   AS ref_table,
                ccu.column_name  AS ref_column,
                rc.delete_rule,
                rc.update_rule,
                kcu.ordinal_position
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
               AND tc.table_schema   = kcu.table_schema
               AND tc.table_name     = kcu.table_name
            JOIN information_schema.referential_constraints rc
                ON rc.constraint_name   = tc.constraint_name
               AND rc.constraint_schema = tc.table_schema
            JOIN information_schema.key_column_usage ccu
                ON ccu.constraint_name  = rc.unique_constraint_name
               AND ccu.table_schema     = rc.unique_constraint_schema
               AND ccu.ordinal_position = kcu.ordinal_position
            WHERE tc.table_schema    = 'public'
              AND tc.table_name      = @tableName
              AND tc.constraint_type = 'FOREIGN KEY'
            ORDER BY tc.constraint_name, kcu.ordinal_position
            """;

        await using (var cmd = new NpgsqlCommand(fkSql, conn))
        {
            cmd.Parameters.AddWithValue("@tableName", tableName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            string? currentConstraint = null;
            ForeignKeyDefinition? currentFk = null;

            while (await reader.ReadAsync(ct))
            {
                var constraintName = reader.GetString(0);
                var colName        = reader.GetString(1);
                var refTable       = reader.GetString(2);
                var refColumn      = reader.GetString(3);
                var deleteRule     = reader.GetString(4);
                var updateRule     = reader.GetString(5);

                if (constraintName != currentConstraint)
                {
                    currentFk = new ForeignKeyDefinition
                    {
                        ConstraintName = constraintName,
                        ReferencedTable = refTable,
                        OnDelete = deleteRule,
                        OnUpdate = updateRule,
                    };
                    schema.ForeignKeys.Add(currentFk);
                    currentConstraint = constraintName;
                }

                currentFk!.Columns.Add(colName);
                currentFk!.ReferencedColumns.Add(refColumn);
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

    public async Task<Dictionary<string, List<string>>> GetViewColumnNamesAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT c.table_name, c.column_name
            FROM information_schema.columns c
            JOIN information_schema.views v
              ON c.table_schema = v.table_schema AND c.table_name = v.table_name
            WHERE c.table_schema = 'public'
            ORDER BY c.table_name, c.ordinal_position
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            var viewName   = reader.GetString(0);
            var columnName = reader.GetString(1);
            if (!result.TryGetValue(viewName, out var cols))
            {
                cols = [];
                result[viewName] = cols;
            }
            cols.Add(columnName);
        }
        return result;
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

    public async Task CanonicalizeIndexesAsync(
        IReadOnlyList<(string TableName, IndexDefinition Index)> indexes,
        CancellationToken ct = default)
    {
        if (indexes.Count == 0)
            return;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 資料表名稱 → 暫存表名稱；null 表示該表的暫存表建立失敗（同表其餘索引直接略過）
        var tempTables = new Dictionary<string, string?>(StringComparer.Ordinal);
        var generator = new SqlGenerator();
        int tableSeq = 0;
        int seq = 0;

        foreach (var (tableName, index) in indexes)
        {
            if (!tempTables.TryGetValue(tableName, out var tempTable))
            {
                // 暫存表在自己的 savepoint 中建立並 release，之後索引探測失敗時回滾到 pgd_canon 不會連帶回滾暫存表
                tempTable = await TryCreateCanonTempTableAsync(conn, tx, tableName, $"pgd_canon_t{tableSeq++}", ct);
                tempTables[tableName] = tempTable;
            }
            if (tempTable == null)
                continue;

            await tx.SaveAsync("pgd_canon", ct);
            try
            {
                // 以 SQL 檔的定義在暫存表上建立同樣的索引，再讀回 PostgreSQL 正規化後的欄位與條件
                var tempIndex = $"pgd_canon_i{seq++}";
                var probe = new IndexDefinition
                {
                    Name = tempIndex,
                    Method = index.Method,
                    Columns = index.Columns,
                    IncludeColumns = index.IncludeColumns,
                    Predicate = index.Predicate,
                };
                await ExecuteAsync(conn, tx, generator.GenerateCreateIndex(tempTable, probe), ct);

                var sql = string.Format(IndexQueryTemplate, "ix.indexrelid = to_regclass(@name)");
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                cmd.Parameters.AddWithValue("@name", $"pg_temp.\"{tempIndex}\"");
                var canonical = (await ReadIndexesAsync(cmd, ct)).Single();

                index.Columns = canonical.Columns;
                index.IncludeColumns = canonical.IncludeColumns;
                index.Predicate = canonical.Predicate;
                await tx.ReleaseAsync("pgd_canon", ct);
            }
            catch (PostgresException)
            {
                // 例如引用了資料庫尚未存在的欄位：保留 SQL 檔原文，交由差異比對判定
                await tx.RollbackAsync("pgd_canon", ct);
            }
        }

        // 只用於取得正規化結果，不留下任何物件
        await tx.RollbackAsync(ct);
    }

    /// <summary>
    /// 以 <c>LIKE public."table"</c> 建立索引正規化用的暫存表（獨立 savepoint）；失敗時回滾該 savepoint 並回傳 null。
    /// </summary>
    private static async Task<string?> TryCreateCanonTempTableAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string tableName, string tempTable, CancellationToken ct)
    {
        await tx.SaveAsync("pgd_canon_table", ct);
        try
        {
            await ExecuteAsync(conn, tx,
                $"CREATE TEMP TABLE \"{tempTable}\" (LIKE public.\"{tableName.Replace("\"", "\"\"")}\")", ct);
            await tx.ReleaseAsync("pgd_canon_table", ct);
            return tempTable;
        }
        catch (PostgresException)
        {
            await tx.RollbackAsync("pgd_canon_table", ct);
            return null;
        }
    }

    public async Task<Dictionary<string, List<DependentObject>>> GetViewDependentsAsync(
        IReadOnlyCollection<string> viewNames,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<DependentObject>>(StringComparer.Ordinal);
        if (viewNames.Count == 0)
            return result;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // 透過 pg_depend → pg_rewrite 遞迴找出依賴各 View 的 View / Materialized View，
        // 也就是 DROP VIEW ... CASCADE 會一併刪除的物件。
        const string sql = """
            WITH RECURSIVE deps(root_name, objid) AS (
                SELECT c.relname::text, c.oid
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relname = ANY(@names)
                  AND c.relkind IN ('v', 'm')
                UNION
                SELECT d.root_name, r.ev_class
                FROM deps d
                JOIN pg_depend dep
                  ON dep.refclassid = 'pg_class'::regclass
                 AND dep.refobjid   = d.objid
                 AND dep.classid    = 'pg_rewrite'::regclass
                JOIN pg_rewrite r ON r.oid = dep.objid
                WHERE r.ev_class <> d.objid
            )
            SELECT DISTINCT d.root_name, n.nspname::text, c.relname::text, c.relkind::text
            FROM deps d
            JOIN pg_class c     ON c.oid = d.objid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE NOT (n.nspname = 'public' AND c.relname = d.root_name)
            ORDER BY 1, 2, 3
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@names", viewNames.ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var root = reader.GetString(0);
            var kind = reader.GetString(3) == "m" ? DependentObject.KindMaterializedView : DependentObject.KindView;

            if (!result.TryGetValue(root, out var list))
            {
                list = [];
                result[root] = list;
            }
            list.Add(new DependentObject(reader.GetString(1), reader.GetString(2), kind));
        }

        return result;
    }

    public async Task<Dictionary<string, List<string>>> GetViewsDependingOnColumnsAsync(
        IReadOnlyCollection<(string TableName, string ColumnName)> columns,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (columns.Count == 0)
            return result;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // 1. pg_depend（refobjid = 表 oid、refobjsubid = attnum、classid = pg_rewrite）→ pg_rewrite.ev_class：直接引用該欄位的 View
        // 2. 再遞迴往上找依賴這些 View 的 View（DROP ... CASCADE 時也會一併刪除）
        const string sql = """
            WITH RECURSIVE cols(tbl, col) AS (
                SELECT * FROM unnest(@tables::text[], @columns::text[])
            ),
            deps(objid, src) AS (
                SELECT r.ev_class, '"' || cols.tbl || '"."' || cols.col || '"'
                FROM cols
                JOIN pg_class t      ON t.relname = cols.tbl AND t.relkind IN ('r', 'p')
                JOIN pg_namespace tn ON tn.oid = t.relnamespace AND tn.nspname = 'public'
                JOIN pg_attribute a  ON a.attrelid = t.oid AND a.attname = cols.col AND NOT a.attisdropped
                JOIN pg_depend dep
                  ON dep.refclassid  = 'pg_class'::regclass
                 AND dep.refobjid    = t.oid
                 AND dep.refobjsubid = a.attnum
                 AND dep.classid     = 'pg_rewrite'::regclass
                JOIN pg_rewrite r ON r.oid = dep.objid
                WHERE r.ev_class <> t.oid
                UNION
                SELECT r.ev_class, d.src
                FROM deps d
                JOIN pg_depend dep
                  ON dep.refclassid = 'pg_class'::regclass
                 AND dep.refobjid   = d.objid
                 AND dep.classid    = 'pg_rewrite'::regclass
                JOIN pg_rewrite r ON r.oid = dep.objid
                WHERE r.ev_class <> d.objid
            )
            SELECT DISTINCT c.relname::text, d.src
            FROM deps d
            JOIN pg_class c     ON c.oid = d.objid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'v'
            ORDER BY 1, 2
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tables", columns.Select(c => c.TableName).ToArray());
        cmd.Parameters.AddWithValue("@columns", columns.Select(c => c.ColumnName).ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var view = reader.GetString(0);
            if (!result.TryGetValue(view, out var list))
            {
                list = [];
                result[view] = list;
            }
            list.Add(reader.GetString(1));
        }

        return result;
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 讀取 <see cref="IndexQueryTemplate"/> 的查詢結果。
    /// </summary>
    private static async Task<List<IndexDefinition>> ReadIndexesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var result = new List<IndexDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var keyCount = reader.GetInt16(3);
            var descFlags = reader.GetFieldValue<bool[]>(4);
            var columnDefs = reader.GetFieldValue<string[]>(5);

            var index = new IndexDefinition
            {
                Name = reader.GetString(0),
                IsUnique = reader.GetBoolean(1),
                Method = reader.GetString(2),
                Predicate = reader.IsDBNull(6) ? null : reader.GetString(6),
            };

            for (int k = 0; k < columnDefs.Length; k++)
            {
                var (name, isExpression) = IndexExpressionNormalizer.ParseColumnToken(columnDefs[k]);
                if (k < keyCount)
                {
                    index.Columns.Add(new IndexColumn
                    {
                        Name = name,
                        IsExpression = isExpression,
                        IsDescending = k < descFlags.Length && descFlags[k],
                    });
                }
                else
                {
                    index.IncludeColumns.Add(name);
                }
            }

            result.Add(index);
        }

        return result;
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
