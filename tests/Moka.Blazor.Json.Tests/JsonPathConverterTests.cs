using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonPathConverterTests
{
    [Theory]
    [InlineData(null, "$")]
    [InlineData("", "$")]
    [InlineData("/", "$")]
    public void ToDotNotation_RootPaths_ReturnsDollar(string? input, string expected)
    {
        Assert.Equal(expected, JsonPathConverter.ToDotNotation(input));
    }

    [Fact]
    public void ToDotNotation_SimpleProperty_ReturnsDotNotation()
    {
        Assert.Equal("$.name", JsonPathConverter.ToDotNotation("/name"));
    }

    [Fact]
    public void ToDotNotation_NestedProperties_ReturnsDotNotation()
    {
        Assert.Equal("$.config.maxDepth", JsonPathConverter.ToDotNotation("/config/maxDepth"));
    }

    [Fact]
    public void ToDotNotation_ArrayIndex_ReturnsBracketNotation()
    {
        Assert.Equal("$.users[0].name", JsonPathConverter.ToDotNotation("/users/0/name"));
    }

    [Fact]
    public void ToDotNotation_MultipleArrayIndices()
    {
        Assert.Equal("$.data[0][1]", JsonPathConverter.ToDotNotation("/data/0/1"));
    }

    [Fact]
    public void ToDotNotation_PropertyWithDot_UsesQuotedBracketNotation()
    {
        Assert.Equal("$[\"special.key\"]", JsonPathConverter.ToDotNotation("/special.key"));
    }

    [Fact]
    public void ToDotNotation_PropertyWithSpace_UsesQuotedBracketNotation()
    {
        Assert.Equal("$[\"my key\"]", JsonPathConverter.ToDotNotation("/my key"));
    }

    [Fact]
    public void ToDotNotation_EscapedJsonPointerSlash_UsesQuotedNotation()
    {
        // ~1 in JSON Pointer is an escaped "/"
        Assert.Equal("$[\"a/b\"]", JsonPathConverter.ToDotNotation("/a~1b"));
    }

    [Fact]
    public void ToDotNotation_EscapedJsonPointerTilde_HandlesCorrectly()
    {
        // ~0 in JSON Pointer is an escaped "~"
        Assert.Equal("$.a~b", JsonPathConverter.ToDotNotation("/a~0b"));
    }

    [Fact]
    public void ToDotNotation_ComplexPath_ReturnsCorrectNotation()
    {
        Assert.Equal("$.users[0].address.city", JsonPathConverter.ToDotNotation("/users/0/address/city"));
    }
}