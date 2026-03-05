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
        Assert.Equal("NewTable", changes[0].TableName);
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
}
