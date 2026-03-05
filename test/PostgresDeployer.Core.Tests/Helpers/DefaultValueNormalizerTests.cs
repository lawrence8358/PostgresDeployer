namespace PostgresDeployer.Core.Tests.Helpers;

using PostgresDeployer.Core.Helpers;

public class DefaultValueNormalizerTests
{
    // ═══ NormalizeFromSql 測試 ═══

    [Theory]
    [InlineData(null, null)]
    [InlineData("FALSE", "FALSE")]
    [InlineData("false", "FALSE")]
    [InlineData("TRUE", "TRUE")]
    [InlineData("true", "TRUE")]
    [InlineData("NOW()", "NOW()")]
    [InlineData("now()", "NOW()")]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("'BabySchool'", "'BabySchool'")]
    public void NormalizeFromSql_VariousValues_ReturnsExpected(string? input, string? expected)
    {
        Assert.Equal(expected, DefaultValueNormalizer.NormalizeFromSql(input));
    }

    [Fact]
    public void NormalizeFromSql_EmptyString_ReturnsNull()
    {
        Assert.Null(DefaultValueNormalizer.NormalizeFromSql(""));
    }

    [Fact]
    public void NormalizeFromSql_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(DefaultValueNormalizer.NormalizeFromSql("   "));
    }

    // ═══ NormalizeFromDb 測試 ═══

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("false", false, "FALSE")]
    [InlineData("true", false, "TRUE")]
    [InlineData("now()", false, "NOW()")]
    [InlineData("0", false, "0")]
    [InlineData("'BabySchool'::character varying", false, "'BabySchool'")]
    [InlineData("'-1'::integer", false, "'-1'")]
    [InlineData("nextval('seq'::regclass)", false, null)]
    [InlineData("anything", true, null)]
    [InlineData(null, true, null)]
    public void NormalizeFromDb_VariousValues_ReturnsExpected(
        string? input, bool isIdentity, string? expected)
    {
        Assert.Equal(expected, DefaultValueNormalizer.NormalizeFromDb(input, isIdentity));
    }

    // ═══ DefaultsMatch 測試 ═══

    [Fact]
    public void DefaultsMatch_BothNull_ReturnsTrue()
        => Assert.True(DefaultValueNormalizer.DefaultsMatch(null, null));

    [Fact]
    public void DefaultsMatch_OneNull_ReturnsFalse()
        => Assert.False(DefaultValueNormalizer.DefaultsMatch("FALSE", null));

    [Fact]
    public void DefaultsMatch_SameValue_ReturnsTrue()
        => Assert.True(DefaultValueNormalizer.DefaultsMatch("FALSE", "FALSE"));

    [Fact]
    public void DefaultsMatch_DifferentCase_ReturnsTrue()
        => Assert.True(DefaultValueNormalizer.DefaultsMatch("false", "FALSE"));

    [Fact]
    public void DefaultsMatch_DifferentValue_ReturnsFalse()
        => Assert.False(DefaultValueNormalizer.DefaultsMatch("FALSE", "TRUE"));
}
