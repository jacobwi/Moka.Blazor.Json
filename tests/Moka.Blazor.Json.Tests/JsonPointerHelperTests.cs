using Moka.Blazor.Json.Utilities;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonPointerHelperTests
{
	[Theory]
	[InlineData("simple", "simple")]
	[InlineData("a/b", "a~1b")]
	[InlineData("a~b", "a~0b")]
	[InlineData("a~/b", "a~0~1b")]
	[InlineData("", "")]
	public void EscapeSegment_ProducesCorrectOutput(string input, string expected) =>
		Assert.Equal(expected, JsonPointerHelper.EscapeSegment(input));

	[Theory]
	[InlineData("simple", "simple")]
	[InlineData("a~1b", "a/b")]
	[InlineData("a~0b", "a~b")]
	[InlineData("a~0~1b", "a~/b")]
	[InlineData("", "")]
	public void UnescapeSegment_ProducesCorrectOutput(string input, string expected) =>
		Assert.Equal(expected, JsonPointerHelper.UnescapeSegment(input));

	[Theory]
	[InlineData("hello")]
	[InlineData("a/b")]
	[InlineData("a~b")]
	[InlineData("~0~1")]
	[InlineData("日本語")]
	[InlineData("🚀")]
	public void RoundTrip_EscapeThenUnescape_ReturnsOriginal(string original) =>
		Assert.Equal(original, JsonPointerHelper.UnescapeSegment(JsonPointerHelper.EscapeSegment(original)));
}
