namespace PostgresDeployer.Core.Tests.Services;

using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public class SchemaDifferTests
{
    private readonly SchemaDiffer _differ = new();

    private static TableSchema CreateTable(string name, params ColumnDefinition[] columns)
    {
        return new TableSchema
        {
            TableName = name,
            Columns = columns.ToList(),
            PrimaryKey = new PrimaryKeyDefinition
            {
                ConstraintName = $"PK_{name}",
                Columns = [columns[0].Name]
            },
            RawSql = $"CREATE TABLE \"{name}\" (...);"
        };
    }

    private static ColumnDefinition Col(
        string name, string type, string rawType,
        bool nullable = true, bool hasDefault = false,
        string? defaultValue = null, bool isIdentity = false,
        int? length = null, int? precision = null, int? scale = null)
    {
        return new ColumnDefinition
        {
            Name = name, Type = type, RawType = rawType,
            IsNullable = nullable, HasDefault = hasDefault,
            DefaultValue = defaultValue, IsIdentity = isIdentity,
            Length = length, Precision = precision, Scale = scale
        };
    }

    private static TableSchema CreateTableWithComment(string name, string? comment, params ColumnDefinition[] columns)
    {
        var table = CreateTable(name, columns);
        table.Comment = comment;
        return table;
    }

    [Fact]
    public void ComputeChanges_NewTable_ReturnsCreateTable()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("NewTable", Col("Id", "UUID", "UUID", nullable: false))
        };
        var actual = new Dictionary<string, TableSchema>();

        var changes = _differ.ComputeChanges(desired, actual);

        Assert.Single(changes);
        Assert.Equal(ChangeType.CreateTable, changes[0].Type);
        Assert.Equal("NewTable", changes[0].EntityName);
        Assert.NotNull(changes[0].Sql);
    }

    [Fact]
    public void ComputeChanges_NewColumn_ReturnsAddColumn()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Phone", "VARCHAR", "VARCHAR(50)", length: 50))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        Assert.Contains(changes, c => c.Type == ChangeType.AddColumn && c.ColumnName == "Phone");
    }

    [Fact]
    public void ComputeChanges_TypeChanged_ReturnsAlterColumnType()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Phone", "VARCHAR", "VARCHAR(100)", length: 100))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Phone", "VARCHAR", "VARCHAR(50)", length: 50))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        Assert.Contains(changes, c => c.Type == ChangeType.AlterColumnType && c.ColumnName == "Phone");
    }

    [Fact]
    public void ComputeChanges_NullableChanged_ReturnsAlterColumnNullable()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Name", "VARCHAR", "VARCHAR(200)", nullable: false, length: 200))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Name", "VARCHAR", "VARCHAR(200)", nullable: true, length: 200))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        Assert.Contains(changes, c => c.Type == ChangeType.AlterColumnNullable && c.ColumnName == "Name");
    }

    [Fact]
    public void ComputeChanges_DefaultChanged_ReturnsAlterColumnDefault()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Active", "BOOLEAN", "BOOLEAN", nullable: false, hasDefault: true, defaultValue: "TRUE"))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Active", "BOOLEAN", "BOOLEAN", nullable: false, hasDefault: true, defaultValue: "FALSE"))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        Assert.Contains(changes, c => c.Type == ChangeType.AlterColumnDefault && c.ColumnName == "Active");
    }

    [Fact]
    public void ComputeChanges_TableCommentChanged_ReturnsAlterTableComment()
    {
        var desired = new List<TableSchema>
        {
            CreateTableWithComment("Users", "使用者資料表",
                Col("Id", "UUID", "UUID", nullable: false))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTableWithComment("Users", "舊註解",
                Col("Id", "UUID", "UUID", nullable: false))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        Assert.Contains(changes, c => c.Type == ChangeType.AlterTableComment && c.EntityName == "Users");
    }

    [Fact]
    public void ComputeChanges_ColumnCommentChanged_ReturnsAlterColumnComment()
    {
        var desiredTable = CreateTable("Users",
            Col("Id", "UUID", "UUID", nullable: false),
            Col("Name", "VARCHAR", "VARCHAR(200)", length: 200));
        desiredTable.Columns.First(c => c.Name == "Name").Comment = "姓名";

        var actualTable = CreateTable("Users",
            Col("Id", "UUID", "UUID", nullable: false),
            Col("Name", "VARCHAR", "VARCHAR(200)", length: 200));
        actualTable.Columns.First(c => c.Name == "Name").Comment = "舊姓名";

        var changes = _differ.ComputeChanges([desiredTable], new Dictionary<string, TableSchema> { ["Users"] = actualTable });

        Assert.Contains(changes, c => c.Type == ChangeType.AlterColumnComment && c.ColumnName == "Name");
    }

    [Fact]
    public void ComputeChanges_ColumnDropped_ReturnsDropColumn()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("OldField", "VARCHAR", "VARCHAR(200)", length: 200))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        var dropChange = changes.FirstOrDefault(c => c.Type == ChangeType.DropColumn);
        Assert.NotNull(dropChange);
        Assert.Equal("OldField", dropChange!.ColumnName);
        Assert.NotNull(dropChange.CautionMessage);
    }

    [Fact]
    public void ComputeChanges_IdenticalSchemas_ReturnsEmpty()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Name", "VARCHAR", "VARCHAR(200)", nullable: true, length: 200))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Name", "VARCHAR", "VARCHAR(200)", nullable: true, length: 200))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        Assert.Empty(changes);
    }

    [Fact]
    public void ComputeChanges_NewNotNullWithoutDefault_ReturnsCautionMessage()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Required", "VARCHAR", "VARCHAR(200)", nullable: false, length: 200))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        var addCol = changes.FirstOrDefault(c => c.Type == ChangeType.AddColumn && c.ColumnName == "Required");
        Assert.NotNull(addCol);
        Assert.NotNull(addCol!.CautionMessage);
    }

    [Fact]
    public void ComputeChanges_NewIndex_ReturnsCreateIndex()
    {
        var desiredTable = CreateTable("Users",
            Col("Id", "UUID", "UUID", nullable: false));
        desiredTable.Indexes.Add(new IndexDefinition
        {
            Name = "IX_Users_Id",
            Columns = [new IndexColumn { Name = "Id" }]
        });

        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false))
        };

        var changes = _differ.ComputeChanges([desiredTable], actual);

        Assert.Contains(changes, c => c.Type == ChangeType.CreateIndex);
    }

    [Fact]
    public void ComputeChanges_IndexChanged_ReturnsRecreateIndex()
    {
        var desiredTable = CreateTable("Users",
            Col("Id", "UUID", "UUID", nullable: false));
        desiredTable.Indexes.Add(new IndexDefinition
        {
            Name = "IX_Test", IsUnique = true,
            Columns = [new IndexColumn { Name = "Id" }]
        });

        var actualTable = CreateTable("Users",
            Col("Id", "UUID", "UUID", nullable: false));
        actualTable.Indexes.Add(new IndexDefinition
        {
            Name = "IX_Test", IsUnique = false,
            Columns = [new IndexColumn { Name = "Id" }]
        });

        var actual = new Dictionary<string, TableSchema> { ["Users"] = actualTable };

        var changes = _differ.ComputeChanges([desiredTable], actual);

        Assert.Contains(changes, c => c.Type == ChangeType.RecreateIndex);
    }

    [Fact]
    public void ComputeChanges_TypeNarrowing_ReturnsCautionMessage()
    {
        var desired = new List<TableSchema>
        {
            CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Name", "VARCHAR", "VARCHAR(100)", length: 100))
        };
        var actual = new Dictionary<string, TableSchema>
        {
            ["Users"] = CreateTable("Users",
                Col("Id", "UUID", "UUID", nullable: false),
                Col("Name", "VARCHAR", "VARCHAR(500)", length: 500))
        };

        var changes = _differ.ComputeChanges(desired, actual);

        var alterType = changes.FirstOrDefault(c => c.Type == ChangeType.AlterColumnType && c.ColumnName == "Name");
        Assert.NotNull(alterType);
        Assert.NotNull(alterType!.CautionMessage);
    }

    #region ComputeViewChanges

    [Fact]
    public void ComputeViewChanges_NewView_ReturnsCreateView()
    {
        const string sql = "CREATE OR REPLACE VIEW \"vw_Test\" AS SELECT 1 AS \"Id\";";
        var desired = new List<(string ViewName, string SqlContent)> { ("vw_Test", sql) };
        var existingCols = new Dictionary<string, List<string>>(); // View 不存在於 DB

        var changes = _differ.ComputeViewChanges(desired, existingCols);

        Assert.Single(changes);
        Assert.Equal(ChangeType.CreateView, changes[0].Type);
        Assert.Equal("vw_Test", changes[0].EntityName);
        Assert.Equal(sql, changes[0].Sql);
    }

    [Fact]
    public void ComputeViewChanges_ExistingViewColumnChanged_ReturnsReplaceView()
    {
        // SQL 欄位：Id, Name（與 DB 不同）
        const string newSql = "CREATE OR REPLACE VIEW \"vw_Test\" AS SELECT 1 AS \"Id\", 'x' AS \"Name\";";
        var desired = new List<(string ViewName, string SqlContent)> { ("vw_Test", newSql) };
        // DB 目前只有 Id 一個欄位
        var existingCols = new Dictionary<string, List<string>>
        {
            ["vw_Test"] = ["Id"]
        };

        var changes = _differ.ComputeViewChanges(desired, existingCols);

        Assert.Single(changes);
        Assert.Equal(ChangeType.ReplaceView, changes[0].Type);
        Assert.Equal("vw_Test", changes[0].EntityName);
        Assert.Equal(newSql, changes[0].Sql);
    }

    [Fact]
    public void ComputeViewChanges_ExistingViewColumnsUnchanged_ReturnsEmpty()
    {
        // SQL 欄位與 DB 一致 → 不應標記為變更
        const string sql = "CREATE OR REPLACE VIEW \"vw_Test\" AS SELECT t.\"Id\", t.\"Name\" FROM \"T\" t;";
        var desired = new List<(string ViewName, string SqlContent)> { ("vw_Test", sql) };
        var existingCols = new Dictionary<string, List<string>>
        {
            ["vw_Test"] = ["Id", "Name"]
        };

        var changes = _differ.ComputeViewChanges(desired, existingCols);

        Assert.Empty(changes);
    }

    [Fact]
    public void ComputeViewChanges_ExistingViewColumnOrderChanged_ReturnsReplaceView()
    {
        // 欄位順序改變 → 42P16 風險 → 應標記為 ReplaceView
        const string sql = "CREATE OR REPLACE VIEW \"vw_Test\" AS SELECT t.\"Name\", t.\"Id\" FROM \"T\" t;";
        var desired = new List<(string ViewName, string SqlContent)> { ("vw_Test", sql) };
        var existingCols = new Dictionary<string, List<string>>
        {
            ["vw_Test"] = ["Id", "Name"]  // DB 中順序是 Id, Name
        };

        var changes = _differ.ComputeViewChanges(desired, existingCols);

        Assert.Single(changes);
        Assert.Equal(ChangeType.ReplaceView, changes[0].Type);
    }

    [Fact]
    public void ComputeViewChanges_MixedViews_ReturnsBothTypes()
    {
        var desired = new List<(string ViewName, string SqlContent)>
        {
            ("vw_New",      "CREATE OR REPLACE VIEW \"vw_New\" AS SELECT 1 AS \"Id\";"),
            ("vw_Changed",  "CREATE OR REPLACE VIEW \"vw_Changed\" AS SELECT 1 AS \"Id\", 2 AS \"NewCol\";"),
            ("vw_Same",     "CREATE OR REPLACE VIEW \"vw_Same\" AS SELECT t.\"Id\" FROM \"T\" t;")
        };
        var existingCols = new Dictionary<string, List<string>>
        {
            ["vw_Changed"] = ["Id"],              // 欄位改變
            ["vw_Same"]    = ["Id"]               // 欄位相同
        };

        var changes = _differ.ComputeViewChanges(desired, existingCols);

        Assert.Equal(2, changes.Count); // vw_New (CreateView) + vw_Changed (ReplaceView)；vw_Same 不加入
        Assert.Equal(ChangeType.CreateView,   changes.First(c => c.EntityName == "vw_New").Type);
        Assert.Equal(ChangeType.ReplaceView,  changes.First(c => c.EntityName == "vw_Changed").Type);
        Assert.DoesNotContain(changes, c => c.EntityName == "vw_Same");
    }

    [Fact]
    public void ComputeViewChanges_SelectStar_ExistingView_ReturnsReplaceView()
    {
        // SELECT * 無法解析欄位，保守地以 ReplaceView 處理（先 DROP 再 CREATE OR REPLACE），避免 42P16
        const string sql = "CREATE OR REPLACE VIEW \"vw_Star\" AS SELECT * FROM \"T\";";
        var desired = new List<(string ViewName, string SqlContent)> { ("vw_Star", sql) };
        var existingCols = new Dictionary<string, List<string>>
        {
            ["vw_Star"] = ["Id", "Name"]
        };

        var changes = _differ.ComputeViewChanges(desired, existingCols);

        Assert.Single(changes);
        Assert.Equal(ChangeType.ReplaceView, changes[0].Type);
        Assert.Equal("vw_Star", changes[0].EntityName);
    }

    [Fact]
    public void ComputeViewChanges_SelectStar_NewView_ReturnsCreateView()
    {
        // SELECT * 且 View 不存在於 DB → 建立（不受無法解析影響）
        const string sql = "CREATE OR REPLACE VIEW \"vw_Star\" AS SELECT * FROM \"T\";";
        var desired = new List<(string ViewName, string SqlContent)> { ("vw_Star", sql) };
        var existingCols = new Dictionary<string, List<string>>(); // View 不存在

        var changes = _differ.ComputeViewChanges(desired, existingCols);

        Assert.Single(changes);
        Assert.Equal(ChangeType.CreateView, changes[0].Type);
    }

    [Fact]
    public void ComputeViewChanges_UnquotedSchemaPrefix_ColumnAdded_ReturnsReplaceView()
    {
        // public."vw_Name" (unquoted schema) — reproduces 42P16 scenario
        const string sql = """
            CREATE OR REPLACE VIEW public."vw_Test" AS
            SELECT p."GroupCodeId", p."CreatedBy" FROM public."T" p;
            """;
        var desired = new List<(string ViewName, string SqlContent)> { ("vw_Test", sql) };
        var existingCols = new Dictionary<string, List<string>>
        {
            ["vw_Test"] = ["CreatedBy"]   // DB has old column set (before GroupCodeId was added)
        };

        var changes = _differ.ComputeViewChanges(desired, existingCols);

        Assert.Single(changes);
        Assert.Equal(ChangeType.ReplaceView, changes[0].Type);
        Assert.Equal("vw_Test", changes[0].EntityName);
    }

    [Fact]
    public void ComputeViewChanges_EmptyDesired_ReturnsEmpty()
    {
        var existingCols = new Dictionary<string, List<string>> { ["vw_Any"] = ["Id"] };
        Assert.Empty(_differ.ComputeViewChanges([], existingCols));
    }

    #endregion

    #region ExtractDesiredColumnNames

    [Theory]
    [InlineData(
        "CREATE OR REPLACE VIEW \"vw\" AS SELECT t.\"Id\", t.\"Name\" FROM \"T\" t;",
        new[] { "Id", "Name" })]
    [InlineData(
        "CREATE OR REPLACE VIEW \"vw\" AS SELECT mItem.\"Code\" AS \"ItemCode\", mGroup.\"Seq\" AS \"GroupSeq\" FROM \"T\" t;",
        new[] { "ItemCode", "GroupSeq" })]
    [InlineData(
        "CREATE OR REPLACE VIEW \"vw\" AS SELECT \"Id\";",
        new[] { "Id" })]
    // unquoted schema prefix: public."vw_name"
    [InlineData(
        "CREATE OR REPLACE VIEW public.\"vw\" AS SELECT p.\"Id\", p.\"Name\" FROM public.\"T\" p;",
        new[] { "Id", "Name" })]
    // quoted schema prefix: "public"."vw_name"
    [InlineData(
        "CREATE OR REPLACE VIEW \"public\".\"vw\" AS SELECT p.\"Id\", p.\"Code\" FROM \"T\" p;",
        new[] { "Id", "Code" })]
    public void ExtractDesiredColumnNames_CommonPatterns_ReturnsNames(string sql, string[] expected)
    {
        var result = SchemaDiffer.ExtractDesiredColumnNames(sql);
        Assert.NotNull(result);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("CREATE OR REPLACE VIEW \"vw\" AS SELECT * FROM \"T\";")]
    public void ExtractDesiredColumnNames_Unparseable_ReturnsNull(string sql)
    {
        var result = SchemaDiffer.ExtractDesiredColumnNames(sql);
        Assert.Null(result);
    }

    #endregion

    #region ComputeChanges_ForeignKey

    private static ForeignKeyDefinition Fk(
        string constraintName, string column, string refTable, string refColumn,
        string onDelete = "NO ACTION", string onUpdate = "NO ACTION")
    {
        return new ForeignKeyDefinition
        {
            ConstraintName = constraintName,
            Columns = [column],
            ReferencedTable = refTable,
            ReferencedColumns = [refColumn],
            OnDelete = onDelete,
            OnUpdate = onUpdate,
        };
    }

    [Fact]
    public void ComputeChanges_NewForeignKey_ReturnsCreateForeignKey()
    {
        var desiredTable = CreateTable("MenuGroup",
            Col("Code", "VARCHAR", "VARCHAR(50)", nullable: false),
            Col("ParentGroupCode", "VARCHAR", "VARCHAR(50)"));
        desiredTable.ForeignKeys.Add(
            Fk("FK_MenuGroup_ParentGroupCode", "ParentGroupCode", "MenuGroup", "Code"));

        var actualTable = CreateTable("MenuGroup",
            Col("Code", "VARCHAR", "VARCHAR(50)", nullable: false),
            Col("ParentGroupCode", "VARCHAR", "VARCHAR(50)"));
        // No FKs in actual

        var changes = _differ.ComputeChanges([desiredTable], new Dictionary<string, TableSchema> { ["MenuGroup"] = actualTable });

        var fkChange = changes.FirstOrDefault(c => c.Type == ChangeType.CreateForeignKey);
        Assert.NotNull(fkChange);
        Assert.Equal("MenuGroup", fkChange!.EntityName);
        Assert.Equal("FK_MenuGroup_ParentGroupCode", fkChange.ColumnName);
    }

    [Fact]
    public void ComputeChanges_UnchangedForeignKey_ReturnsEmpty()
    {
        var fk = Fk("FK_A", "ParentId", "Parent", "Id");

        var desiredTable = CreateTable("Child", Col("Id", "INT", "INT", nullable: false), Col("ParentId", "INT", "INT"));
        desiredTable.ForeignKeys.Add(fk);

        var actualTable = CreateTable("Child", Col("Id", "INT", "INT", nullable: false), Col("ParentId", "INT", "INT"));
        actualTable.ForeignKeys.Add(Fk("FK_A", "ParentId", "Parent", "Id"));

        var changes = _differ.ComputeChanges([desiredTable], new Dictionary<string, TableSchema> { ["Child"] = actualTable });

        Assert.DoesNotContain(changes, c =>
            c.Type == ChangeType.CreateForeignKey ||
            c.Type == ChangeType.DropForeignKey ||
            c.Type == ChangeType.RecreateForeignKey);
    }

    [Fact]
    public void ComputeChanges_ForeignKeyDropped_ReturnsDropForeignKeyWithCaution()
    {
        var desiredTable = CreateTable("Child",
            Col("Id", "INT", "INT", nullable: false), Col("ParentId", "INT", "INT"));
        // No FK in desired

        var actualTable = CreateTable("Child",
            Col("Id", "INT", "INT", nullable: false), Col("ParentId", "INT", "INT"));
        actualTable.ForeignKeys.Add(Fk("FK_Child_Parent", "ParentId", "Parent", "Id"));

        var changes = _differ.ComputeChanges([desiredTable], new Dictionary<string, TableSchema> { ["Child"] = actualTable });

        var dropFk = changes.FirstOrDefault(c => c.Type == ChangeType.DropForeignKey);
        Assert.NotNull(dropFk);
        Assert.Equal("FK_Child_Parent", dropFk!.ColumnName);
        Assert.NotNull(dropFk.CautionMessage);
    }

    [Fact]
    public void ComputeChanges_ForeignKeyDefinitionChanged_ReturnsRecreateForeignKey()
    {
        var desiredTable = CreateTable("Child",
            Col("Id", "INT", "INT", nullable: false), Col("ParentId", "INT", "INT"));
        desiredTable.ForeignKeys.Add(Fk("FK_A", "ParentId", "Parent", "Id", onDelete: "CASCADE"));

        var actualTable = CreateTable("Child",
            Col("Id", "INT", "INT", nullable: false), Col("ParentId", "INT", "INT"));
        actualTable.ForeignKeys.Add(Fk("FK_A", "ParentId", "Parent", "Id", onDelete: "NO ACTION"));

        var changes = _differ.ComputeChanges([desiredTable], new Dictionary<string, TableSchema> { ["Child"] = actualTable });

        var recreateFk = changes.FirstOrDefault(c => c.Type == ChangeType.RecreateForeignKey);
        Assert.NotNull(recreateFk);
        Assert.Equal("FK_A", recreateFk!.ColumnName);
        Assert.NotNull(recreateFk.CautionMessage);
    }

    #endregion
}
