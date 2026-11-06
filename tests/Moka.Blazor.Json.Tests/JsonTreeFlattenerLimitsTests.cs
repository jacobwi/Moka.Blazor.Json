using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonTreeFlattenerLimitsTests
{
	private static async Task<JsonDocumentManager> ParseAsync(string json)
	{
		var manager = new JsonDocumentManager(NullLogger<JsonDocumentManager>.Instance,
			Options.Create(new MokaJsonViewerOptions()));
		await manager.ParseAsync(json);
		return manager;
	}

	private static string NumberArray(int count) => $"[{string.Join(',', Enumerable.Range(0, count))}]";

	[Fact]
	public async Task Containers_Over_The_Group_Size_Show_Groups()
	{
		await using JsonDocumentManager source = await ParseAsync(NumberArray(25));
		var flattener = new JsonTreeFlattener(groupSize: 10);
		flattener.Expand("");

		List<FlattenedJsonNode> rows = flattener.Flatten(source);

		// Root, three groups, closing bracket
		Assert.Equal(5, rows.Count);
		Assert.Equal([0, 10, 20], rows.Where(r => r.IsRangeGroup).Select(r => r.RangeStart));
		Assert.Equal([10, 10, 5], rows.Where(r => r.IsRangeGroup).Select(r => r.ChildCount));
		Assert.All(rows.Where(r => r.IsRangeGroup), r => Assert.False(r.IsExpanded));
	}

	[Fact]
	public async Task Opening_A_Group_Shows_Only_Its_Children()
	{
		await using JsonDocumentManager source = await ParseAsync(NumberArray(25));
		var flattener = new JsonTreeFlattener(groupSize: 10);
		flattener.Expand("");

		FlattenedJsonNode second = flattener.Flatten(source).First(r => r.IsRangeGroup && r.RangeStart == 10);
		flattener.ToggleExpand(second.Path);
		List<FlattenedJsonNode> rows = flattener.Flatten(source);

		List<FlattenedJsonNode> children = rows.Where(r => r is { IsRangeGroup: false, Depth: 2 }).ToList();
		Assert.Equal(Enumerable.Range(10, 10), children.Select(r => r.ArrayIndex));
		Assert.Equal("/10", children[0].Path);
		Assert.All(children, r => Assert.True(r.HasTrailingComma));
		Assert.Equal(Enumerable.Range(0, rows.Count), rows.Select(r => r.Id));
	}

	[Fact]
	public async Task The_Last_Child_Of_The_Last_Group_Has_No_Comma()
	{
		await using JsonDocumentManager source = await ParseAsync(NumberArray(25));
		var flattener = new JsonTreeFlattener(groupSize: 10);
		flattener.Expand("");

		FlattenedJsonNode last = flattener.Flatten(source).Last(r => r.IsRangeGroup);
		flattener.ToggleExpand(last.Path);

		FlattenedJsonNode lastChild = flattener.Flatten(source).Last(r => r is { IsRangeGroup: false, IsClosingBracket: false });
		Assert.Equal("/24", lastChild.Path);
		Assert.False(lastChild.HasTrailingComma);
	}

	[Theory]
	[InlineData("""{"items":[REPLACE]}""", "/items/17", 10)]
	[InlineData("""{"items":[REPLACE]}""", "/items/0", 0)]
	public async Task Reveal_Opens_The_Group_Holding_The_Path(string template, string path, int groupStart)
	{
		string json = template.Replace("REPLACE", string.Join(',', Enumerable.Range(0, 25)), StringComparison.Ordinal);
		await using JsonDocumentManager source = await ParseAsync(json);
		var flattener = new JsonTreeFlattener(groupSize: 10);

		flattener.RevealPath(source, path);
		List<FlattenedJsonNode> rows = flattener.Flatten(source);

		Assert.Contains(rows, r => r.Path == path);
		Assert.Single(rows, r => r is { IsRangeGroup: true, IsExpanded: true });
		Assert.Contains(rows, r => r is { IsRangeGroup: true, IsExpanded: true } && r.RangeStart == groupStart);
	}

	[Fact]
	public async Task Reveal_Finds_A_Key_In_A_Grouped_Object()
	{
		string properties = string.Join(',', Enumerable.Range(0, 25).Select(i => $"\"k{i}\":{i}"));
		await using JsonDocumentManager source = await ParseAsync($"{{{properties}}}");
		var flattener = new JsonTreeFlattener(groupSize: 10);

		flattener.RevealPath(source, "/k23");

		Assert.Contains(flattener.Flatten(source), r => r.Path == "/k23");
	}

	[Fact]
	public async Task Collapsing_A_Subtree_Closes_Its_Groups()
	{
		await using JsonDocumentManager source = await ParseAsync($$"""{"a":{{NumberArray(25)}}}""");
		var flattener = new JsonTreeFlattener(groupSize: 10);
		flattener.RevealPath(source, "/a/5");

		flattener.CollapseSubtree("/a");
		flattener.Expand("/a");

		Assert.DoesNotContain(flattener.Flatten(source), r => r is { IsRangeGroup: true, IsExpanded: true });
	}

	[Fact]
	public async Task Expand_All_Reports_When_The_Row_Limit_Stops_It_And_Keeps_The_Top_Open()
	{
		await using JsonDocumentManager source = await ParseAsync("""
		                                                          {"a":{"b":{"c":{"d":1}}},"e":{"f":[1,2,3]}}
		                                                          """);
		var flattener = new JsonTreeFlattener();

		bool truncated = flattener.ExpandAll(source, new JsonExpandLimits(MaxRows: 8));

		Assert.True(truncated);
		Assert.Contains("", flattener.ExpandedPaths);
		Assert.Contains("/a", flattener.ExpandedPaths);
		Assert.Contains("/e", flattener.ExpandedPaths);
		Assert.DoesNotContain("/a/b/c", flattener.ExpandedPaths);
	}

	[Fact]
	public async Task Expand_All_Does_Not_Report_Truncation_When_Everything_Fits()
	{
		await using JsonDocumentManager source = await ParseAsync("""{"a":{"b":[1]},"c":{}}""");
		var flattener = new JsonTreeFlattener();

		Assert.False(flattener.ExpandAll(source));
		Assert.Equal(["", "/a", "/a/b", "/c"], flattener.ExpandedPaths.Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task First_View_Leaves_Very_Long_Children_Closed_But_Always_Opens_The_Root()
	{
		await using JsonDocumentManager source = await ParseAsync($$"""{"long":{{NumberArray(50)}},"short":[1,2]}""");
		var flattener = new JsonTreeFlattener();

		flattener.ExpandToDepth(source, 2, new JsonExpandLimits(1000, MaxChildren: 1));

		Assert.Contains("", flattener.ExpandedPaths);
		Assert.DoesNotContain("/long", flattener.ExpandedPaths);
		Assert.DoesNotContain("/short", flattener.ExpandedPaths);

		await using JsonDocumentManager wide = await ParseAsync(NumberArray(50));
		flattener.ExpandToDepth(wide, 2, new JsonExpandLimits(1000, MaxChildren: 1));
		Assert.Contains("", flattener.ExpandedPaths);
	}

	[Fact]
	public async Task Grouped_Root_Counts_Its_Groups_Rather_Than_Its_Children_Against_The_Limit()
	{
		await using JsonDocumentManager source = await ParseAsync(NumberArray(1000));
		var flattener = new JsonTreeFlattener(groupSize: 100);

		Assert.False(flattener.ExpandAll(source, new JsonExpandLimits(MaxRows: 20)));
		Assert.Equal(12, flattener.Flatten(source).Count);
	}

	[Fact]
	public async Task Expand_All_Can_Be_Cancelled_And_Leaves_The_View_As_It_Was()
	{
		await using JsonDocumentManager source = await ParseAsync("""{"a":{"b":1}}""");
		var flattener = new JsonTreeFlattener();
		flattener.Expand("/a");
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		Assert.ThrowsAny<OperationCanceledException>(() => flattener.ExpandAll(source, null, cts.Token));
		Assert.Equal(["/a"], flattener.ExpandedPaths);
	}

	[Fact]
	public async Task Flatten_Does_Not_Hold_The_Lock_While_It_Reads()
	{
		await using JsonDocumentManager inner = await ParseAsync("""{"a":1}""");
		var flattener = new JsonTreeFlattener();
		flattener.Expand("");

		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var slow = new BlockingSource(inner, entered, release);

		Task<List<FlattenedJsonNode>> flatten = Task.Run(() => flattener.Flatten(slow));
		Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

		// A click on the UI thread while the background flatten is reading.
		await Task.Run(() => flattener.ToggleExpand("/a")).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		release.Set();
		Assert.Equal(3, (await flatten).Count);
	}

	private sealed class BlockingSource(
		IJsonDocumentSource inner,
		ManualResetEventSlim entered,
		ManualResetEventSlim release) : IJsonDocumentSource
	{
		public bool IsLoaded => inner.IsLoaded;
		public long DocumentSizeBytes => inner.DocumentSizeBytes;
		public TimeSpan ParseTime => inner.ParseTime;
		public JsonValueKind RootValueKind => inner.RootValueKind;
		public bool SupportsEditing => inner.SupportsEditing;
		public int GetChildCount(string path) => inner.GetChildCount(path);
		public IEnumerable<JsonChildDescriptor> EnumerateChildren(string path) => inner.EnumerateChildren(path);

		public IEnumerable<JsonChildDescriptor> EnumerateChildren(string path, int start, int count)
		{
			entered.Set();
			release.Wait(TimeSpan.FromSeconds(10));
			return inner.EnumerateChildren(path, start, count);
		}

		public int GetChildIndex(string path, string escapedSegment) => inner.GetChildIndex(path, escapedSegment);
		public string? GetRawValue(string path) => inner.GetRawValue(path);
		public JsonValueKind GetValueKind(string path) => inner.GetValueKind(path);
		public long GetByteLength(string path) => inner.GetByteLength(path);
		public JsonElement GetElement(string path) => inner.GetElement(path);
		public string GetJsonString(bool indented = true) => inner.GetJsonString(indented);
		public void WriteJson(Stream destination, bool indented = true) => inner.WriteJson(destination, indented);
		public long CountNodes() => inner.CountNodes();
		public int GetMaxDepth() => inner.GetMaxDepth();
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
