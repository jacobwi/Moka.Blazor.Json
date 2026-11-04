using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class AuditRegressionTests
{
	private static JsonDocumentManager CreateManager() =>
		new(NullLogger<JsonDocumentManager>.Instance, Options.Create(new MokaJsonViewerOptions()));

	private static Task<LazyJsonDocumentSource> CreateLazyAsync(string json) =>
		LazyJsonDocumentSource.CreateFromStringAsync(
			json, NullLogger<LazyJsonDocumentSource>.Instance, new MokaJsonViewerOptions());

	#region Search engine

	[Fact]
	public void Regex_Search_Reports_Key_And_Value_Match_Once()
	{
		var engine = new JsonSearchEngine();
		using var doc = JsonDocument.Parse("""{"error":"error happened"}""");

		int count = engine.Search(doc.RootElement, "err", new JsonSearchOptions { UseRegex = true });

		Assert.Equal(1, count);
		Assert.Equal(["/error"], engine.MatchPaths);
	}

	[Fact]
	public async Task FindMatches_Only_Searches_Under_Root_Path()
	{
		var engine = new JsonSearchEngine();
		await using JsonDocumentManager manager = CreateManager();
		await manager.ParseAsync("""{"a":{"x":"hit"},"b":{"x":"hit"}}""");

		IReadOnlyList<string> matches = engine.FindMatches(manager, "/b", "hit");

		Assert.Equal(["/b/x"], matches);
		Assert.Equal(0, engine.MatchCount);
	}

	[Fact]
	public void Catastrophic_Regex_Returns_No_Matches_Instead_Of_Throwing()
	{
		var engine = new JsonSearchEngine();
		string value = new string('a', 40) + "!";
		using var doc = JsonDocument.Parse($$"""{"v":"{{value}}"}""");

		int count = engine.Search(doc.RootElement, "^(a+)+$", new JsonSearchOptions { UseRegex = true });

		Assert.Equal(0, count);
	}

	[Fact]
	public void Next_And_Previous_Wrap_Around()
	{
		var engine = new JsonSearchEngine();
		engine.SetMatches(["/a", "/b", "/c"]);

		Assert.Equal("/a", engine.ActiveMatchPath);
		Assert.Equal("/b", engine.NextMatch());
		Assert.Equal("/c", engine.NextMatch());
		Assert.Equal("/a", engine.NextMatch());
		Assert.Equal("/c", engine.PreviousMatch());
	}

	#endregion

	#region Lazy index

	[Fact]
	public async Task Lazy_Source_Indexes_Array_Primitives_At_Their_Own_Index()
	{
		await using LazyJsonDocumentSource source = await CreateLazyAsync("""{"items":[1,{"a":1,"b":2},3]}""");

		Assert.Equal(JsonValueKind.Number, source.GetValueKind("/items/0"));
		Assert.Equal(JsonValueKind.Object, source.GetValueKind("/items/1"));
		Assert.Equal(2, source.GetChildCount("/items/1"));
		Assert.Equal("1", source.GetRawValue("/items/0"));
		Assert.Equal("3", source.GetRawValue("/items/2"));
	}

	[Fact]
	public async Task Lazy_Source_Search_Finds_Values_Inside_Array_Objects()
	{
		var engine = new JsonSearchEngine();
		await using LazyJsonDocumentSource source = await CreateLazyAsync("""{"items":[1,{"name":"needle"},3]}""");

		int count = engine.Search(source, "needle");

		Assert.Equal(1, count);
		Assert.Equal("/items/1/name", engine.ActiveMatchPath);
	}

	[Fact]
	public async Task Lazy_Minified_Output_Is_Strict_Minified_Json()
	{
		await using LazyJsonDocumentSource source = await CreateLazyAsync("""
		                                                                    {
		                                                                      // comment
		                                                                      "a": [1, 2,],
		                                                                    }
		                                                                    """);

		Assert.Equal("""{"a":[1,2]}""", source.GetJsonString(false));
	}

	#endregion

	#region Editing

	[Fact]
	public async Task Rename_Onto_Existing_Key_Is_Rejected()
	{
		await using JsonDocumentManager manager = CreateManager();
		await manager.ParseAsync("""{"a":1,"b":2}""");

		Assert.Throws<InvalidOperationException>(() => manager.RenameKeyAtPath("/a", "b"));
	}

	[Fact]
	public async Task Replace_Value_Uses_Unescaped_Key()
	{
		await using JsonDocumentManager manager = CreateManager();
		await manager.ParseAsync("""{"a/b":1,"c~d":2}""");

		using var result = JsonDocument.Parse(manager.ReplaceValueAtPath("/a~1b", "99"));

		Assert.Equal(99, result.RootElement.GetProperty("a/b").GetInt32());
		Assert.Equal(2, result.RootElement.EnumerateObject().Count());
	}

	[Fact]
	public async Task Edits_Work_On_Documents_With_Comments_And_Trailing_Commas()
	{
		await using JsonDocumentManager manager = CreateManager();
		await manager.ParseAsync("""
		                         {
		                           // comment
		                           "a": [1, 2,],
		                         }
		                         """);

		using var result = JsonDocument.Parse(manager.ReplaceValueAtPath("/a/0", "5"));

		Assert.Equal(5, result.RootElement.GetProperty("a")[0].GetInt32());
	}

	[Fact]
	public async Task Edits_Do_Not_Escape_Non_Ascii_Text()
	{
		await using JsonDocumentManager manager = CreateManager();
		await manager.ParseAsync("""{"greeting":"héllo <b>","n":1}""");

		string edited = manager.ReplaceValueAtPath("/n", "2");

		Assert.Contains("héllo <b>", edited);
		Assert.Contains("héllo <b>", manager.GetJsonString());
		Assert.Contains("héllo <b>", JsonSorter.SortKeys(manager.RootElement));
	}

	[Theory]
	[InlineData("1,000")]
	[InlineData("NaN")]
	[InlineData(".5")]
	public void Number_Validation_Rejects_Non_Json_Numbers(string input) =>
		Assert.Equal("Invalid number", JsonEditValidator.ValidateValue(input, JsonValueKind.Number));

	[Fact]
	public void Edit_History_Undoes_And_Redoes_The_Latest_Edit()
	{
		var history = new EditHistory();
		history.PushSnapshot("s0");
		history.PushSnapshot("s1");
		history.PushSnapshot("s2");

		Assert.Equal("s1", history.Undo());
		Assert.Equal("s0", history.Undo());
		Assert.Equal("s1", history.Redo());
		Assert.Equal("s2", history.Redo());
		Assert.False(history.CanRedo);
	}

	#endregion

	#region Tree flattener

	[Fact]
	public void ExpandAncestors_Expands_Every_Parent_But_Not_The_Node()
	{
		var flattener = new JsonTreeFlattener();

		flattener.ExpandAncestors("/a/b~1c/0");

		Assert.Equal(new HashSet<string>(["", "/a", "/a/b~1c"]), flattener.ExpandedPaths);
	}

	[Fact]
	public void CollapseSubtree_Removes_Node_And_Descendants_Only()
	{
		var flattener = new JsonTreeFlattener();
		flattener.RestoreExpandedPaths(["", "/a", "/a/b", "/ab"]);

		flattener.CollapseSubtree("/a");

		Assert.Equal(new HashSet<string>(["", "/ab"]), flattener.ExpandedPaths);
	}

	#endregion
}
