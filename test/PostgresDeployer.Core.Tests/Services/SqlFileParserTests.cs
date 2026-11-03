namespace PostgresDeployer.Core.Tests.Services;

using PostgresDeployer.Core.Services;

public class SqlFileParserTests
{
    private readonly SqlFileParser _parser = new();

    private string GetTestDataPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestData", "Tables", fileName);

    // ═══ SimpleTable 測試 ═══

    [Fact]
    public void ParseTableFile_SimpleTable_ReturnsCorrectTableName()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("SimpleTable.sql"));
        Assert.Equal("Test_Simple", schema.TableName);
        Assert.False(schema.IfNotExists);
    }

    [Fact]
    public void ParseTableFile_SimpleTable_ParsesThreeColumns()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("SimpleTable.sql"));
        Assert.Equal(3, schema.Columns.Count);
    }

    [Fact]
    public void ParseTableFile_SimpleTable_ParsesUuidColumn()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("SimpleTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Id");
        Assert.Equal("UUID", col.Type);
        Assert.False(col.IsNullable);
        Assert.False(col.IsIdentity);
        Assert.False(col.HasDefault);
    }

    [Fact]
    public void ParseTableFile_SimpleTable_ParsesVarcharColumn()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("SimpleTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Name");
        Assert.Equal("VARCHAR", col.Type);
        Assert.Equal(200, col.Length);
        Assert.True(col.IsNullable);
    }

    [Fact]
    public void ParseTableFile_SimpleTable_ParsesIdentityColumn()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("SimpleTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Cix");
        Assert.Equal("INT", col.Type);
        Assert.True(col.IsIdentity);
        Assert.False(col.IsNullable);
    }

    [Fact]
    public void ParseTableFile_SimpleTable_ParsesNamedPrimaryKey()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("SimpleTable.sql"));
        Assert.NotNull(schema.PrimaryKey);
        Assert.Equal("PK_Test_Simple", schema.PrimaryKey!.ConstraintName);
        Assert.Single(schema.PrimaryKey.Columns);
        Assert.Equal("Id", schema.PrimaryKey.Columns[0]);
    }

    [Fact]
    public void ParseTableFile_SimpleTable_ParsesOneIndex()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("SimpleTable.sql"));
        Assert.Single(schema.Indexes);
        Assert.Equal("CIX_Test_Simple", schema.Indexes[0].Name);
        Assert.False(schema.Indexes[0].IsUnique);
        Assert.Single(schema.Indexes[0].Columns);
        Assert.Equal("Cix", schema.Indexes[0].Columns[0].Name);
        Assert.False(schema.Indexes[0].Columns[0].IsDescending);
    }

    // ═══ ComplexTable 測試 ═══

    [Fact]
    public void ParseTableFile_ComplexTable_Parses15Columns()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        Assert.Equal(15, schema.Columns.Count);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesDefaultFalse()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Active");
        Assert.True(col.HasDefault);
        Assert.Equal("FALSE", col.DefaultValue);
        Assert.False(col.IsNullable);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesDecimalWithPrecisionAndScale()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Score");
        Assert.Equal("DECIMAL", col.Type);
        Assert.Equal(18, col.Precision);
        Assert.Equal(10, col.Scale);
        Assert.True(col.IsNullable);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesDecimalWithPrecisionOnly()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Rating");
        Assert.Equal("DECIMAL", col.Type);
        Assert.Equal(8, col.Precision);
        Assert.Null(col.Scale);
        Assert.True(col.HasDefault);
        Assert.Equal("0", col.DefaultValue);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesDecimalWithoutParams()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Amount");
        Assert.Equal("DECIMAL", col.Type);
        Assert.Null(col.Precision);
        Assert.Null(col.Scale);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesNcharAsChar()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Code");
        Assert.Equal("CHAR", col.Type);
        Assert.Equal(10, col.Length);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesDefaultNow()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var col = schema.Columns.First(c => c.Name == "CreatedDate");
        Assert.True(col.HasDefault);
        Assert.Equal("NOW()", col.DefaultValue);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesBigintIdentity()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Cix");
        Assert.Equal("BIGINT", col.Type);
        Assert.True(col.IsIdentity);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesDescIndex()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var cixIdx = schema.Indexes.First(i => i.Name == "CIX_Test_Complex");
        Assert.True(cixIdx.Columns[0].IsDescending);
    }

    [Fact]
    public void ParseTableFile_ComplexTable_ParsesUniqueMultiColumnIndex()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("ComplexTable.sql"));
        var idx = schema.Indexes.First(i => i.Name == "IX_Test_Complex_Account");
        Assert.True(idx.IsUnique);
        Assert.Equal(2, idx.Columns.Count);
        Assert.Equal("Account", idx.Columns[0].Name);
        Assert.Equal("Type", idx.Columns[1].Name);
    }

    // ═══ CompoundPkTable 測試 ═══

    [Fact]
    public void ParseTableFile_CompoundPk_ParsesMultipleKeyColumns()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("CompoundPkTable.sql"));
        Assert.NotNull(schema.PrimaryKey);
        Assert.Equal(2, schema.PrimaryKey!.Columns.Count);
        Assert.Equal("RoleCode", schema.PrimaryKey.Columns[0]);
        Assert.Equal("UserId", schema.PrimaryKey.Columns[1]);
    }

    [Fact]
    public void ParseTableFile_CompoundPk_ParsesDefaultTrue()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("CompoundPkTable.sql"));
        var col = schema.Columns.First(c => c.Name == "Active");
        Assert.True(col.HasDefault);
        Assert.Equal("TRUE", col.DefaultValue);
    }

    // ═══ InlinePkTable 測試 ═══

    [Fact]
    public void ParseTableFile_InlinePk_DetectsInlinePrimaryKey()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("InlinePkTable.sql"));
        Assert.NotNull(schema.PrimaryKey);
        Assert.Null(schema.PrimaryKey!.ConstraintName);
        Assert.Single(schema.PrimaryKey.Columns);
        Assert.Equal("Code", schema.PrimaryKey.Columns[0]);
    }

    [Fact]
    public void ParseTableFile_InlinePk_NoIndexes()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("InlinePkTable.sql"));
        Assert.Empty(schema.Indexes);
    }

    // ═══ IfNotExistsTable 測試 ═══

    [Fact]
    public void ParseTableFile_IfNotExists_SetsFlag()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("IfNotExistsTable.sql"));
        Assert.True(schema.IfNotExists);
    }

    [Fact]
    public void ParseTableFile_IfNotExists_ParsesIndex()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("IfNotExistsTable.sql"));
        Assert.Single(schema.Indexes);
        Assert.Equal("IX_Test_IfNotExists_Name", schema.Indexes[0].Name);
    }

    // ═══ AllTypesTable 測試 ═══

    [Fact]
    public void ParseTableFile_AllTypes_Parses16Columns()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("AllTypesTable.sql"));
        Assert.Equal(16, schema.Columns.Count);
    }

    [Fact]
    public void ParseTableFile_AllTypes_ParsesAllBaseTypes()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("AllTypesTable.sql"));
        var types = schema.Columns.Select(c => c.Type).ToList();
        Assert.Contains("UUID", types);
        Assert.Contains("VARCHAR", types);
        Assert.Contains("CHAR", types);
        Assert.Contains("TEXT", types);
        Assert.Contains("SMALLINT", types);
        Assert.Contains("INT", types);
        Assert.Contains("BIGINT", types);
        Assert.Contains("BOOLEAN", types);
        Assert.Contains("DECIMAL", types);
        Assert.Contains("TIMESTAMPTZ", types);
        Assert.Contains("TIMESTAMP", types);
        Assert.Contains("DATE", types);
        Assert.Contains("BYTEA", types);
    }

    // ═══ 目錄解析測試 ═══

    [Fact]
    public void ParseTableDirectory_ExistingDir_ReturnsAllSchemas()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Tables");
        var schemas = _parser.ParseTableDirectory(dir);
        Assert.True(schemas.Count >= 5);
    }

    [Fact]
    public void ParseTableDirectory_NonExistentDir_ReturnsEmpty()
    {
        var schemas = _parser.ParseTableDirectory("/nonexistent/path");
        Assert.Empty(schemas);
    }

    // ═══ ReadSqlFiles 測試 ═══

    [Fact]
    public void ReadSqlFiles_ExistingDir_ReturnsFileNameAndContent()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Tables");
        var files = _parser.ReadSqlFiles(dir);
        Assert.True(files.Count >= 5);
        Assert.All(files, f =>
        {
            Assert.False(string.IsNullOrEmpty(f.FileName));
            Assert.False(string.IsNullOrEmpty(f.Content));
            Assert.EndsWith(".sql", f.FileName);
        });
    }

    [Fact]
    public void ReadSqlFiles_NonExistentDir_ReturnsEmpty()
    {
        var files = _parser.ReadSqlFiles("/nonexistent/path");
        Assert.Empty(files);
    }

    // ═══ RawSql 保留測試 ═══

    [Fact]
    public void ParseTableFile_PreservesRawSql()
    {
        var schema = _parser.ParseTableFile(GetTestDataPath("SimpleTable.sql"));
        Assert.NotNull(schema.RawSql);
        Assert.Contains("CREATE TABLE", schema.RawSql);
    }

    // ═══ FOREIGN KEY 解析測試 ═══

    [Fact]
    public void ParseTableSql_ForeignKeyConstraint_ParsedCorrectly()
    {
        const string sql = """
            CREATE TABLE "MenuGroup" (
                "Code" VARCHAR(50) NOT NULL,
                "ParentGroupCode" VARCHAR(50) NULL,
                CONSTRAINT "PK_MenuGroup" PRIMARY KEY ("Code"),
                CONSTRAINT "FK_MenuGroup_ParentGroupCode" FOREIGN KEY ("ParentGroupCode") REFERENCES "MenuGroup"("Code")
            );
            """;

        var schema = _parser.ParseTableSql(sql);

        Assert.Single(schema.ForeignKeys);
        var fk = schema.ForeignKeys[0];
        Assert.Equal("FK_MenuGroup_ParentGroupCode", fk.ConstraintName);
        Assert.Equal(["ParentGroupCode"], fk.Columns);
        Assert.Equal("MenuGroup", fk.ReferencedTable);
        Assert.Equal(["Code"], fk.ReferencedColumns);
        Assert.Equal("NO ACTION", fk.OnDelete);
        Assert.Equal("NO ACTION", fk.OnUpdate);
    }

    [Fact]
    public void ParseTableSql_ForeignKeyWithCascade_ParsedCorrectly()
    {
        const string sql = """
            CREATE TABLE "Order" (
                "Id" UUID NOT NULL,
                "UserId" UUID NOT NULL,
                CONSTRAINT "PK_Order" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_Order_UserId" FOREIGN KEY ("UserId") REFERENCES "User"("Id") ON DELETE CASCADE ON UPDATE NO ACTION
            );
            """;

        var schema = _parser.ParseTableSql(sql);

        Assert.Single(schema.ForeignKeys);
        var fk = schema.ForeignKeys[0];
        Assert.Equal("FK_Order_UserId", fk.ConstraintName);
        Assert.Equal("User", fk.ReferencedTable);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Equal("NO ACTION", fk.OnUpdate);
    }

    [Fact]
    public void ParseTableSql_NoForeignKeys_ForeignKeysEmpty()
    {
        const string sql = """
            CREATE TABLE "Simple" (
                "Id" UUID NOT NULL,
                CONSTRAINT "PK_Simple" PRIMARY KEY ("Id")
            );
            """;

        var schema = _parser.ParseTableSql(sql);

        Assert.Empty(schema.ForeignKeys);
    }

    [Fact]
    public void ParseTableSql_WithComments_ParsesTableAndColumnComments()
    {
        const string sql = """
            CREATE TABLE "Base_Auth_MenuItem" (
                "RoleCode" VARCHAR(50) NOT NULL,
                "ItemCode" VARCHAR(50) NOT NULL,
                CONSTRAINT "PK_Base_Auth_MenuItem" PRIMARY KEY ("RoleCode", "ItemCode")
            );

            COMMENT ON TABLE "Base_Auth_MenuItem" IS '角色可使用的功能選單資料表';
            COMMENT ON COLUMN "Base_Auth_MenuItem"."RoleCode" IS '角色代碼 Base_Auth_Role.Code';
            COMMENT ON COLUMN "Base_Auth_MenuItem"."ItemCode" IS '選單代碼 Base_Setting_MenuItem.Code';
            """;

        var schema = _parser.ParseTableSql(sql);

        Assert.Equal("角色可使用的功能選單資料表", schema.Comment);
        Assert.Equal("角色代碼 Base_Auth_Role.Code", schema.Columns.First(c => c.Name == "RoleCode").Comment);
        Assert.Equal("選單代碼 Base_Setting_MenuItem.Code", schema.Columns.First(c => c.Name == "ItemCode").Comment);
    }
}
