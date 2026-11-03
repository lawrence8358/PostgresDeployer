namespace PostgresDeployer.Core.Tests.Helpers;

using PostgresDeployer.Core.Helpers;

public class IndexExpressionNormalizerTests
{
    [Theory]
    [InlineData("\"ProjectId\"", "ProjectId", false)]
    [InlineData("cix", "cix", false)]
    [InlineData("Cix", "cix", false)]
    [InlineData("lower(\"Email\")", "lower(\"Email\")", true)]
    [InlineData("\"Name\" text_pattern_ops", "\"Name\" text_pattern_ops", true)]
    public void ParseColumnToken_ReturnsNameAndExpressionFlag(string token, string expectedName, bool expectedExpression)
    {
        var (name, isExpression) = IndexExpressionNormalizer.ParseColumnToken(token);
        Assert.Equal(expectedName, name);
        Assert.Equal(expectedExpression, isExpression);
    }

    [Theory]
    [InlineData("NOT \"IsOldBranch\"", "(NOT \"IsOldBranch\")")]
    [InlineData("\"DeletedAt\" IS NULL", "((\"DeletedAt\"   is null))")]
    [InlineData("\"Status\" = 'A B'", "(\"Status\" = 'A B')")]
    public void FragmentsEqual_EquivalentText_ReturnsTrue(string a, string b)
    {
        Assert.True(IndexExpressionNormalizer.FragmentsEqual(a, b));
    }

    [Theory]
    [InlineData("NOT \"IsOldBranch\"", "\"IsOldBranch\"")]
    [InlineData("\"Status\" = 'A B'", "\"Status\" = 'AB'")]
    [InlineData("\"Name\" = 'x'", "\"name\" = 'x'")]
    [InlineData("NOT \"A\"", null)]
    public void FragmentsEqual_DifferentText_ReturnsFalse(string a, string? b)
    {
        Assert.False(IndexExpressionNormalizer.FragmentsEqual(a, b));
    }

    [Fact]
    public void Normalize_DoesNotStripParensThatDoNotWrapWholeExpression()
    {
        Assert.Equal("(\"a\")and(\"b\")", IndexExpressionNormalizer.Normalize("(\"a\") AND (\"b\")"));
    }
}
