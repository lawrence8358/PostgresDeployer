namespace PostgresDeployer.Core.Tests.Helpers;

using PostgresDeployer.Core.Helpers;

public class TypeNormalizerTests
{
    // ═══ ParseSqlType 測試 ═══

    [Theory]
    [InlineData("VARCHAR(200)", "VARCHAR", 200, null, null)]
    [InlineData("UUID", "UUID", null, null, null)]
    [InlineData("INT", "INT", null, null, null)]
    [InlineData("SMALLINT", "SMALLINT", null, null, null)]
    [InlineData("BIGINT", "BIGINT", null, null, null)]
    [InlineData("BOOLEAN", "BOOLEAN", null, null, null)]
    [InlineData("TIMESTAMPTZ", "TIMESTAMPTZ", null, null, null)]
    [InlineData("TIMESTAMP", "TIMESTAMP", null, null, null)]
    [InlineData("DATE", "DATE", null, null, null)]
    [InlineData("TEXT", "TEXT", null, null, null)]
    [InlineData("BYTEA", "BYTEA", null, null, null)]
    [InlineData("DECIMAL(18,10)", "DECIMAL", null, 18, 10)]
    [InlineData("DECIMAL(8)", "DECIMAL", null, 8, null)]
    [InlineData("DECIMAL", "DECIMAL", null, null, null)]
    [InlineData("NCHAR(10)", "CHAR", 10, null, null)]
    [InlineData("VARCHAR(500)", "VARCHAR", 500, null, null)]
    public void ParseSqlType_VariousTypes_ReturnsCorrectResult(
        string input, string expectedType, int? expectedLen, int? expectedPrec, int? expectedScale)
    {
        var result = TypeNormalizer.ParseSqlType(input);
        Assert.Equal(expectedType, result.BaseType);
        Assert.Equal(expectedLen, result.Length);
        Assert.Equal(expectedPrec, result.Precision);
        Assert.Equal(expectedScale, result.Scale);
    }

    // ═══ NormalizeFromDb 測試 ═══

    [Theory]
    [InlineData("character varying", "varchar", 200, null, null, "VARCHAR", 200, null, null)]
    [InlineData("integer", "int4", null, 32, 0, "INT", null, null, null)]
    [InlineData("smallint", "int2", null, 16, 0, "SMALLINT", null, null, null)]
    [InlineData("bigint", "int8", null, 64, 0, "BIGINT", null, null, null)]
    [InlineData("boolean", "bool", null, null, null, "BOOLEAN", null, null, null)]
    [InlineData("uuid", "uuid", null, null, null, "UUID", null, null, null)]
    [InlineData("timestamp with time zone", "timestamptz", null, null, null, "TIMESTAMPTZ", null, null, null)]
    [InlineData("numeric", "numeric", null, 18, 10, "DECIMAL", null, 18, 10)]
    [InlineData("text", "text", null, null, null, "TEXT", null, null, null)]
    [InlineData("bytea", "bytea", null, null, null, "BYTEA", null, null, null)]
    public void NormalizeFromDb_VariousDbTypes_ReturnsCorrectResult(
        string dataType, string udtName, int? charMaxLen, int? numPrec, int? numScale,
        string expectedType, int? expectedLen, int? expectedPrec, int? expectedScale)
    {
        var result = TypeNormalizer.NormalizeFromDb(dataType, udtName, charMaxLen, numPrec, numScale);
        Assert.Equal(expectedType, result.BaseType);
        Assert.Equal(expectedLen, result.Length);
        Assert.Equal(expectedPrec, result.Precision);
        Assert.Equal(expectedScale, result.Scale);
    }

    // ═══ TypesMatch 測試 ═══

    [Fact]
    public void TypesMatch_SameVarchar_ReturnsTrue()
    {
        Assert.True(TypeNormalizer.TypesMatch(
            ("VARCHAR", 200, null, null), ("VARCHAR", 200, null, null)));
    }

    [Fact]
    public void TypesMatch_DifferentVarcharLength_ReturnsFalse()
    {
        Assert.False(TypeNormalizer.TypesMatch(
            ("VARCHAR", 200, null, null), ("VARCHAR", 500, null, null)));
    }

    [Fact]
    public void TypesMatch_SameDecimal_ReturnsTrue()
    {
        Assert.True(TypeNormalizer.TypesMatch(
            ("DECIMAL", null, 18, 10), ("DECIMAL", null, 18, 10)));
    }

    [Fact]
    public void TypesMatch_DifferentBaseType_ReturnsFalse()
    {
        Assert.False(TypeNormalizer.TypesMatch(
            ("INT", null, null, null), ("BIGINT", null, null, null)));
    }

    [Fact]
    public void TypesMatch_SameInt_ReturnsTrue()
    {
        Assert.True(TypeNormalizer.TypesMatch(
            ("INT", null, null, null), ("INT", null, null, null)));
    }

    [Fact]
    public void TypesMatch_SameUuid_ReturnsTrue()
    {
        Assert.True(TypeNormalizer.TypesMatch(
            ("UUID", null, null, null), ("UUID", null, null, null)));
    }

    // ═══ IsTypeNarrowing 測試 ═══

    [Fact]
    public void IsTypeNarrowing_VarcharShorter_ReturnsTrue()
    {
        Assert.True(TypeNormalizer.IsTypeNarrowing(
            ("VARCHAR", 200, null, null), ("VARCHAR", 500, null, null)));
    }

    [Fact]
    public void IsTypeNarrowing_VarcharLonger_ReturnsFalse()
    {
        Assert.False(TypeNormalizer.IsTypeNarrowing(
            ("VARCHAR", 500, null, null), ("VARCHAR", 200, null, null)));
    }

    [Fact]
    public void IsTypeNarrowing_BigintToInt_ReturnsTrue()
    {
        Assert.True(TypeNormalizer.IsTypeNarrowing(
            ("INT", null, null, null), ("BIGINT", null, null, null)));
    }

    [Fact]
    public void IsTypeNarrowing_IntToBigint_ReturnsFalse()
    {
        Assert.False(TypeNormalizer.IsTypeNarrowing(
            ("BIGINT", null, null, null), ("INT", null, null, null)));
    }

    [Fact]
    public void IsTypeNarrowing_DecimalPrecisionSmaller_ReturnsTrue()
    {
        Assert.True(TypeNormalizer.IsTypeNarrowing(
            ("DECIMAL", null, 8, null), ("DECIMAL", null, 18, 10)));
    }
}
