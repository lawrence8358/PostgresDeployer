namespace PostgresDeployer.Core.Tests.Services;

using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public class SqlGeneratorTests
{
    private readonly SqlGenerator _gen = new();

    [Fact]
    public void GenerateAddColumn_NullableVarchar_CorrectSql()
    {
        var col = new ColumnDefinition
        {
            Name = "Phone", RawType = "VARCHAR(50)", IsNullable = true
        };
        var sql = _gen.GenerateAddColumn("Users", col);
        Assert.Equal("ALTER TABLE \"Users\" ADD COLUMN \"Phone\" VARCHAR(50);", sql);
    }

    [Fact]
    public void GenerateAddColumn_NotNullWithDefault_CorrectSql()
    {
        var col = new ColumnDefinition
        {
            Name = "Active", RawType = "BOOLEAN",
            IsNullable = false, HasDefault = true, DefaultValue = "FALSE"
        };
        var sql = _gen.GenerateAddColumn("Users", col);
        Assert.Equal("ALTER TABLE \"Users\" ADD COLUMN \"Active\" BOOLEAN NOT NULL DEFAULT FALSE;", sql);
    }

    [Fact]
    public void GenerateAddColumn_Identity_CorrectSql()
    {
        var col = new ColumnDefinition
        {
            Name = "Cix", RawType = "INT",
            IsNullable = false, IsIdentity = true
        };
        var sql = _gen.GenerateAddColumn("Users", col);
        Assert.Equal("ALTER TABLE \"Users\" ADD COLUMN \"Cix\" INT NOT NULL GENERATED ALWAYS AS IDENTITY;", sql);
    }

    [Fact]
    public void GenerateAlterColumnType_IncludesUsingClause()
    {
        var sql = _gen.GenerateAlterColumnType("Users", "Phone", "VARCHAR(100)");
        Assert.Equal(
            "ALTER TABLE \"Users\" ALTER COLUMN \"Phone\" TYPE VARCHAR(100) USING \"Phone\"::VARCHAR(100);",
            sql);
    }

    [Fact]
    public void GenerateAlterColumnNullable_SetNotNull_CorrectSql()
    {
        var sql = _gen.GenerateAlterColumnNullable("Users", "Name", false);
        Assert.Equal("ALTER TABLE \"Users\" ALTER COLUMN \"Name\" SET NOT NULL;", sql);
    }

    [Fact]
    public void GenerateAlterColumnNullable_DropNotNull_CorrectSql()
    {
        var sql = _gen.GenerateAlterColumnNullable("Users", "Name", true);
        Assert.Equal("ALTER TABLE \"Users\" ALTER COLUMN \"Name\" DROP NOT NULL;", sql);
    }

    [Fact]
    public void GenerateAlterColumnDefault_SetDefault_CorrectSql()
    {
        var sql = _gen.GenerateAlterColumnDefault("Users", "Active", "FALSE");
        Assert.Equal("ALTER TABLE \"Users\" ALTER COLUMN \"Active\" SET DEFAULT FALSE;", sql);
    }

    [Fact]
    public void GenerateAlterColumnDefault_DropDefault_CorrectSql()
    {
        var sql = _gen.GenerateAlterColumnDefault("Users", "Active", null);
        Assert.Equal("ALTER TABLE \"Users\" ALTER COLUMN \"Active\" DROP DEFAULT;", sql);
    }

    [Fact]
    public void GenerateAlterTableComment_WithText_EscapesSingleQuote()
    {
        var sql = _gen.GenerateAlterTableComment("Users", "owner's table");
        Assert.Equal("COMMENT ON TABLE \"Users\" IS 'owner''s table';", sql);
    }

    [Fact]
    public void GenerateAlterColumnComment_Null_RemovesComment()
    {
        var sql = _gen.GenerateAlterColumnComment("Users", "Name", null);
        Assert.Equal("COMMENT ON COLUMN \"Users\".\"Name\" IS NULL;", sql);
    }

    [Fact]
    public void GenerateCreateIndex_SimpleIndex_CorrectSql()
    {
        var idx = new IndexDefinition
        {
            Name = "IX_Users_Name",
            Columns = [new IndexColumn { Name = "Name" }]
        };
        var sql = _gen.GenerateCreateIndex("Users", idx);
        Assert.Equal("CREATE INDEX IF NOT EXISTS \"IX_Users_Name\" ON \"Users\" (\"Name\");", sql);
    }

    [Fact]
    public void GenerateCreateIndex_UniqueDescIndex_CorrectSql()
    {
        var idx = new IndexDefinition
        {
            Name = "IX_Users_Cix", IsUnique = true,
            Columns = [new IndexColumn { Name = "Cix", IsDescending = true }]
        };
        var sql = _gen.GenerateCreateIndex("Users", idx);
        Assert.Equal("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Users_Cix\" ON \"Users\" (\"Cix\" DESC);", sql);
    }

    [Fact]
    public void GenerateCreateIndex_MultiColumnIndex_CorrectSql()
    {
        var idx = new IndexDefinition
        {
            Name = "IX_Users_Multi",
            Columns =
            [
                new IndexColumn { Name = "Account" },
                new IndexColumn { Name = "Type" }
            ]
        };
        var sql = _gen.GenerateCreateIndex("Users", idx);
        Assert.Equal(
            "CREATE INDEX IF NOT EXISTS \"IX_Users_Multi\" ON \"Users\" (\"Account\", \"Type\");",
            sql);
    }

    [Fact]
    public void GenerateDropIndex_CorrectSql()
    {
        var sql = _gen.GenerateDropIndex("IX_Old_Index");
        Assert.Equal("DROP INDEX IF EXISTS \"IX_Old_Index\";", sql);
    }

    [Fact]
    public void GenerateDropColumn_CorrectSql()
    {
        var sql = _gen.GenerateDropColumn("Users", "OldField");
        Assert.Equal("ALTER TABLE \"Users\" DROP COLUMN \"OldField\";", sql);
    }

    [Fact]
    public void GenerateDropPrimaryKey_CorrectSql()
    {
        var sql = _gen.GenerateDropPrimaryKey("Users", "PK_Users");
        Assert.Equal("ALTER TABLE \"Users\" DROP CONSTRAINT \"PK_Users\";", sql);
    }

    [Fact]
    public void GenerateCreatePrimaryKey_SingleColumn_CorrectSql()
    {
        var pk = new PrimaryKeyDefinition
        {
            ConstraintName = "PK_Users",
            Columns = ["Id"]
        };
        var sql = _gen.GenerateCreatePrimaryKey("Users", pk);
        Assert.Equal("ALTER TABLE \"Users\" ADD CONSTRAINT \"PK_Users\" PRIMARY KEY (\"Id\");", sql);
    }

    [Fact]
    public void GenerateCreatePrimaryKey_CompoundKey_CorrectSql()
    {
        var pk = new PrimaryKeyDefinition
        {
            ConstraintName = "PK_UserRoles",
            Columns = ["UserId", "RoleCode"]
        };
        var sql = _gen.GenerateCreatePrimaryKey("UserRoles", pk);
        Assert.Equal(
            "ALTER TABLE \"UserRoles\" ADD CONSTRAINT \"PK_UserRoles\" PRIMARY KEY (\"UserId\", \"RoleCode\");",
            sql);
    }
}
