namespace PostgresDeployer.Core.Tests.Services;

using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public class SqlSchemaDetectorTests
{
    // ═══ CREATE TABLE ═══

    [Fact]
    public void Detect_CreateTable_ReturnsTable()
    {
        var sql = """
            CREATE TABLE "users" (
                "id" INT NOT NULL
            );
            """;
        Assert.Equal(SchemaFileType.Table, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateTableIfNotExists_ReturnsTable()
    {
        var sql = """CREATE TABLE IF NOT EXISTS "orders" ("id" INT);""";
        Assert.Equal(SchemaFileType.Table, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateTable_CaseInsensitive_ReturnsTable()
    {
        var sql = """create table "products" ("id" int);""";
        Assert.Equal(SchemaFileType.Table, SqlSchemaDetector.Detect(sql));
    }

    // ═══ CREATE VIEW ═══

    [Fact]
    public void Detect_CreateView_ReturnsView()
    {
        var sql = """CREATE VIEW "active_users" AS SELECT * FROM "users";""";
        Assert.Equal(SchemaFileType.View, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateOrReplaceView_ReturnsView()
    {
        var sql = """CREATE OR REPLACE VIEW "active_users" AS SELECT * FROM "users";""";
        Assert.Equal(SchemaFileType.View, SqlSchemaDetector.Detect(sql));
    }

    // ═══ CREATE SEQUENCE ═══

    [Fact]
    public void Detect_CreateSequence_ReturnsSequence()
    {
        var sql = """
            CREATE SEQUENCE "order_id_seq"
                START WITH 1
                INCREMENT BY 1;
            """;
        Assert.Equal(SchemaFileType.Sequence, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateSequenceIfNotExists_ReturnsSequence()
    {
        var sql = """CREATE IF NOT EXISTS SEQUENCE "order_id_seq" START WITH 1;""";
        Assert.Equal(SchemaFileType.Sequence, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateSequence_CaseInsensitive_ReturnsSequence()
    {
        var sql = """create sequence "my_seq" start with 100;""";
        Assert.Equal(SchemaFileType.Sequence, SqlSchemaDetector.Detect(sql));
    }

    // ═══ CREATE FUNCTION ═══

    [Fact]
    public void Detect_CreateFunction_ReturnsFunction()
    {
        var sql = """
            CREATE FUNCTION get_user(p_id INT)
            RETURNS TABLE(id INT, name TEXT)
            LANGUAGE plpgsql AS $$
            BEGIN
                RETURN QUERY SELECT id, name FROM users WHERE id = p_id;
            END;
            $$;
            """;
        Assert.Equal(SchemaFileType.Function, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateOrReplaceFunction_ReturnsFunction()
    {
        var sql = """
            CREATE OR REPLACE FUNCTION now_utc()
            RETURNS TIMESTAMPTZ
            LANGUAGE sql AS $$ SELECT NOW() AT TIME ZONE 'UTC'; $$;
            """;
        Assert.Equal(SchemaFileType.Function, SqlSchemaDetector.Detect(sql));
    }

    // ═══ CREATE PROCEDURE ═══

    [Fact]
    public void Detect_CreateProcedure_ReturnsProcedure()
    {
        var sql = """
            CREATE PROCEDURE archive_old_orders(p_days INT)
            LANGUAGE plpgsql AS $$
            BEGIN
                DELETE FROM orders WHERE created_at < NOW() - (p_days || ' days')::INTERVAL;
            END;
            $$;
            """;
        Assert.Equal(SchemaFileType.Procedure, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateOrReplaceProcedure_ReturnsProcedure()
    {
        var sql = """
            CREATE OR REPLACE PROCEDURE sync_data()
            LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM 1;
            END;
            $$;
            """;
        Assert.Equal(SchemaFileType.Procedure, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateProcedure_CaseInsensitive_ReturnsProcedure()
    {
        var sql = """create or replace procedure do_something() language plpgsql as $$ begin end; $$;""";
        Assert.Equal(SchemaFileType.Procedure, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_CreateProcedure_WithOutParam_ReturnsProcedure()
    {
        var sql = """
            CREATE OR REPLACE PROCEDURE sp_auth_resetfailurecount(
                IN  p_id        UUID,
                OUT p_success   BOOLEAN
            )
            LANGUAGE plpgsql AS $$
            BEGIN
                UPDATE "Base_Auth_User"
                SET "FailureCount" = 0,
                    "LockDate"     = NULL
                WHERE "Id" = p_id;

                p_success := FOUND;
            END;
            $$;
            """;
        Assert.Equal(SchemaFileType.Procedure, SqlSchemaDetector.Detect(sql));
    }

    // ═══ Unknown ═══

    [Fact]
    public void Detect_UnrecognizedContent_ReturnsUnknown()
    {
        var sql = "-- This is just a comment\nSELECT 1;";
        Assert.Equal(SchemaFileType.Unknown, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_EmptyString_ReturnsUnknown()
    {
        Assert.Equal(SchemaFileType.Unknown, SqlSchemaDetector.Detect(""));
    }

    [Fact]
    public void Detect_InsertStatement_ReturnsUnknown()
    {
        var sql = "INSERT INTO users (name) VALUES ('Alice');";
        Assert.Equal(SchemaFileType.Unknown, SqlSchemaDetector.Detect(sql));
    }

    // ═══ 前置空白/換行 ═══

    [Fact]
    public void Detect_WithLeadingWhitespace_ReturnsCorrectType()
    {
        var sql = "\n\n  CREATE TABLE \"foo\" (\"id\" INT);";
        Assert.Equal(SchemaFileType.Table, SqlSchemaDetector.Detect(sql));
    }

    [Fact]
    public void Detect_WithLeadingComment_ThenView_ReturnsView()
    {
        var sql = """
            -- 建立使用者視圖
            CREATE OR REPLACE VIEW "v_users" AS SELECT id FROM users;
            """;
        Assert.Equal(SchemaFileType.View, SqlSchemaDetector.Detect(sql));
    }
}
