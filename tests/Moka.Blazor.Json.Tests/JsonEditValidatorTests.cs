using System.Text.Json;
using System.Text.Json.Nodes;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonEditValidatorTests
{
	#region ValidateValue

	[Theory]
	[InlineData("42", JsonValueKind.Number)]
	[InlineData("-3.14", JsonValueKind.Number)]
	[InlineData("0", JsonValueKind.Number)]
	[InlineData("1e10", JsonValueKind.Number)]
	public void ValidateValue_Valid_Numbers_Return_Null(string input, JsonValueKind kind) =>
		Assert.Null(JsonEditValidator.ValidateValue(input, kind));

	[Theory]
	[InlineData("abc", JsonValueKind.Number)]
	[InlineData("", JsonValueKind.Number)]
	[InlineData("12.34.56", JsonValueKind.Number)]
	public void ValidateValue_Invalid_Numbers_Return_Error(string input, JsonValueKind kind)
	{
		string? error = JsonEditValidator.ValidateValue(input, kind);
		Assert.NotNull(error);
		Assert.Equal("Invalid number", error);
	}

	[Theory]
	[InlineData("hello")]
	[InlineData("")]
	[InlineData("any string")]
	public void ValidateValue_Strings_Always_Valid(string input) =>
		Assert.Null(JsonEditValidator.ValidateValue(input, JsonValueKind.String));

	[Theory]
	[InlineData("true", JsonValueKind.True)]
	[InlineData("false", JsonValueKind.False)]
	[InlineData("true", JsonValueKind.False)]
	[InlineData("false", JsonValueKind.True)]
	public void ValidateValue_Valid_Booleans_Return_Null(string input, JsonValueKind kind) =>
		Assert.Null(JsonEditValidator.ValidateValue(input, kind));

	[Theory]
	[InlineData("yes", JsonValueKind.True)]
	[InlineData("1", JsonValueKind.True)]
	[InlineData("True", JsonValueKind.True)]
	public void ValidateValue_Invalid_Booleans_Return_Error(string input, JsonValueKind kind)
	{
		string? error = JsonEditValidator.ValidateValue(input, kind);
		Assert.NotNull(error);
	}

	[Fact]
	public void ValidateValue_Null_Accepts_Null_String() =>
		Assert.Null(JsonEditValidator.ValidateValue("null", JsonValueKind.Null));

	[Fact]
	public void ValidateValue_Null_Rejects_Other_Values() =>
		Assert.NotNull(JsonEditValidator.ValidateValue("something", JsonValueKind.Null));

	#endregion

	#region ValidatePropertyName

	[Fact]
	public void ValidatePropertyName_Valid_Name_Returns_Null()
	{
		var obj = new JsonObject { ["existing"] = 1 };
		Assert.Null(JsonEditValidator.ValidatePropertyName("newKey", obj, null));
	}

	[Fact]
	public void ValidatePropertyName_Same_Name_Returns_Null()
	{
		var obj = new JsonObject { ["myKey"] = 1 };
		Assert.Null(JsonEditValidator.ValidatePropertyName("myKey", obj, "myKey"));
	}

	[Fact]
	public void ValidatePropertyName_Empty_Returns_Error()
	{
		var obj = new JsonObject();
		string? error = JsonEditValidator.ValidatePropertyName("", obj, null);
		Assert.NotNull(error);
		Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ValidatePropertyName_Whitespace_Returns_Error()
	{
		var obj = new JsonObject();
		Assert.NotNull(JsonEditValidator.ValidatePropertyName("   ", obj, null));
	}

	[Fact]
	public void ValidatePropertyName_Duplicate_Returns_Error()
	{
		var obj = new JsonObject { ["taken"] = 1 };
		string? error = JsonEditValidator.ValidatePropertyName("taken", obj, "other");
		Assert.NotNull(error);
		Assert.Contains("Duplicate", error, StringComparison.OrdinalIgnoreCase);
	}

	#endregion
}
