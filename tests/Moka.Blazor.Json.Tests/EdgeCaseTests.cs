using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;
using Moka.Blazor.Json.Utilities;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class EdgeCaseTests
{
	private static async Task<JsonDocumentManager> CreateManagerAsync(string json)
	{
		var manager = new JsonDocumentManager(
			NullLogger<JsonDocumentManager>.Instance, Options.Create(new MokaJsonViewerOptions()));
		await manager.ParseAsync(json);
		return manager;
	}

	private static Task<LazyJsonDocumentSource> CreateLazyAsync(string json) =>
		LazyJsonDocumentSource.CreateFromStringAsync(
			json, NullLogger<LazyJsonDocumentSource>.Instance, new MokaJsonViewerOptions());

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "Ownership passes to the caller, which disposes the source.")]
	private static async Task<IJsonDocumentSource> CreateSourceAsync(bool lazy, string json) =>
		lazy ? await CreateLazyAsync(json) : await CreateManagerAsync(json);

	#region Null values

	[Theory]
	[InlineData("42", "42")]
	[InlineData(" true ", "true")]
	[InlineData("null", "null")]
	[InlineData("hello", "\"hello\"")]
	[InlineData("\"quoted\"", "\"quoted\"")]
	[InlineData("{\"x\":1}", "{\"x\":1}")]
	[InlineData("1,000", "\"1,000\"")]
	[InlineData("", "\"\"")]
	public void Null_Edit_Input_Is_Stored_As_Its_Json_Type(string input, string expected) =>
		Assert.Equal(expected, JsonEditValidator.ToJsonLiteral(input, JsonValueKind.Null));

	[Fact]
	public async Task Null_Value_Can_Be_Replaced()
	{
		await using JsonDocumentManager manager = await CreateManagerAsync("""{"a":null,"list":[null]}""");

		using var obj = JsonDocument.Parse(manager.ReplaceValueAtPath("/a", "5"));
		using var arr = JsonDocument.Parse(manager.ReplaceValueAtPath("/list/0", "\"x\""));

		Assert.Equal(5, obj.RootElement.GetProperty("a").GetInt32());
		Assert.Equal("x", arr.RootElement.GetProperty("list")[0].GetString());
	}

	[Fact]
	public async Task Replacing_A_Missing_Property_Throws_Instead_Of_Adding_It()
	{
		await using JsonDocumentManager manager = await CreateManagerAsync("""{"a":1}""");

		Assert.Throws<KeyNotFoundException>(() => manager.ReplaceValueAtPath("/b", "2"));
	}

	#endregion

	#region Empty key

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Empty_Key_Is_A_Property_Not_The_Root(bool lazy)
	{
		IJsonDocumentSource source = await CreateSourceAsync(lazy, """{"":5,"a":{"":6}}""");
		await using (source)
		{
			List<JsonChildDescriptor> children = source.EnumerateChildren("").ToList();

			Assert.Equal(["/", "/a"], children.Select(c => c.Path));
			Assert.Equal("", children[0].PropertyName);
			Assert.Equal(JsonValueKind.Object, source.GetValueKind(""));
			Assert.Equal(JsonValueKind.Number, source.GetValueKind("/"));
			Assert.Equal("5", source.GetRawValue("/"));
			Assert.Equal("6", source.GetRawValue("/a/"));
			Assert.Equal(["/a/"], source.EnumerateChildren("/a").Select(c => c.Path));
		}
	}

	[Fact]
	public async Task Empty_Key_Can_Be_Edited_And_Removed()
	{
		await using JsonDocumentManager manager = await CreateManagerAsync("""{"":5,"a":1}""");

		using var replaced = JsonDocument.Parse(manager.ReplaceValueAtPath("/", "7"));
		using var removed = JsonDocument.Parse(manager.RemoveNodeAtPath("/"));

		Assert.Equal(7, replaced.RootElement.GetProperty("").GetInt32());
		Assert.Equal(["a"], removed.RootElement.EnumerateObject().Select(p => p.Name));
	}

	[Theory]
	[InlineData("/", "$[\"\"]")]
	[InlineData("/a/", "$.a[\"\"]")]
	[InlineData("/ 1", "$[\" 1\"]")]
	[InlineData("/a\\b", "$[\"a\\\\b\"]")]
	public void Path_Display_Handles_Empty_And_Odd_Keys(string jsonPointer, string expected) =>
		Assert.Equal(expected, JsonPathConverter.ToDotNotation(jsonPointer));

	[Fact]
	public void Pointer_Split_Keeps_Empty_Segments()
	{
		Assert.Empty(JsonPointerHelper.Split(""));
		Assert.Equal([""], JsonPointerHelper.Split("/"));
		Assert.Equal(["a", "", "b"], JsonPointerHelper.Split("/a//b"));
	}

	#endregion

	#region Duplicate keys

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Repeated_Key_Shows_Once_With_The_Last_Value(bool lazy)
	{
		IJsonDocumentSource source = await CreateSourceAsync(lazy, """{"a":1,"b":2,"a":3}""");
		await using (source)
		{
			List<JsonChildDescriptor> children = source.EnumerateChildren("").ToList();

			Assert.Equal(["/a", "/b"], children.Select(c => c.Path));
			Assert.Equal("3", children[0].RawValue);
			Assert.Equal("3", source.GetRawValue("/a"));
			Assert.Equal(2, source.GetChildCount(""));
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Repeated_Key_Deep_In_The_Document_Is_Collapsed(bool lazy)
	{
		IJsonDocumentSource source = await CreateSourceAsync(lazy, """{"x":{"y":{"k":1,"k":{"z":2}}}}""");
		await using (source)
		{
			JsonChildDescriptor y = Assert.Single(source.EnumerateChildren("/x"));
			Assert.Equal(1, y.ChildCount);

			JsonChildDescriptor k = Assert.Single(source.EnumerateChildren("/x/y"));
			Assert.Equal(JsonValueKind.Object, k.ValueKind);
			Assert.Equal(JsonValueKind.Object, source.GetValueKind("/x/y/k"));
		}
	}

	[Fact]
	public async Task Lazy_Index_Replaces_An_Earlier_Container_With_A_Later_Value()
	{
		await using LazyJsonDocumentSource source = await CreateLazyAsync("""{"a":{"inner":1},"a":2}""");

		Assert.Equal(JsonValueKind.Number, source.GetValueKind("/a"));
		Assert.Equal("2", source.GetRawValue("/a"));
		Assert.Equal(1, source.GetChildCount(""));
	}

	[Fact]
	public async Task Documents_With_Repeated_Keys_Can_Be_Edited()
	{
		await using JsonDocumentManager manager = await CreateManagerAsync("""{"a":1,"b":2,"a":3}""");

		using var result = JsonDocument.Parse(manager.ReplaceValueAtPath("/b", "9"));

		Assert.Equal(["a", "b"], result.RootElement.EnumerateObject().Select(p => p.Name));
		Assert.Equal(3, result.RootElement.GetProperty("a").GetInt32());
		Assert.Equal(9, result.RootElement.GetProperty("b").GetInt32());
	}

	[Fact]
	public void Sort_Keeps_One_Copy_Of_A_Repeated_Key()
	{
		using var doc = JsonDocument.Parse("""{"b":1,"a":2,"b":3}""");

		Assert.Equal("""{"a":2,"b":3}""", JsonSorter.SortKeys(doc.RootElement, indented: false));
	}

	[Theory]
	[InlineData("""{"a":1,"b":{"a":2},"c":[{"a":1,"b":2}]}""", false)]
	[InlineData("""{"a":1,"b":2,"a":3}""", true)]
	[InlineData("""[{"x":1},{"y":{"k":1,"k":2}}]""", true)]
	public void Duplicate_Key_Scan_Finds_Repeats(string json, bool expected) =>
		Assert.Equal(expected, JsonObjectReader.MayContainDuplicateKeys(System.Text.Encoding.UTF8.GetBytes(json)));

	#endregion
}
