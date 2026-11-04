using System.Text.Json;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonStructuralIndexTests
{
	[Fact]
	public async Task Build_SimpleObject_IndexesRootAndChildren()
	{
		byte[] json = """{"a":1,"b":"hello","c":true}"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		Assert.True(index.TryGetEntry("", out JsonStructuralIndex.IndexEntry root));
		Assert.Equal(JsonValueKind.Object, root.ValueKind);
		Assert.Equal(3, root.ChildCount);
	}

	[Fact]
	public async Task Build_SimpleArray_IndexesRootAndElements()
	{
		byte[] json = """[1,2,3]"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		Assert.True(index.TryGetEntry("", out JsonStructuralIndex.IndexEntry root));
		Assert.Equal(JsonValueKind.Array, root.ValueKind);
		Assert.Equal(3, root.ChildCount);
	}

	[Fact]
	public async Task Build_NestedObject_IndexesFirstLevel()
	{
		byte[] json = """{"user":{"name":"Alice","age":30},"count":1}"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		// Root should be indexed with 2 children
		Assert.True(index.TryGetEntry("", out JsonStructuralIndex.IndexEntry root));
		Assert.Equal(2, root.ChildCount);

		// /user should be indexed (it's at depth 1, within limit)
		Assert.True(index.TryGetEntry("/user", out JsonStructuralIndex.IndexEntry user));
		Assert.Equal(JsonValueKind.Object, user.ValueKind);

		// Primitives are read from their parent, not indexed
		Assert.False(index.TryGetEntry("/count", out _));
	}

	[Fact]
	public async Task Build_DeeplyNested_SkipsBeyondLimit()
	{
		byte[] json = """{"a":{"b":{"c":{"d":1}}}}"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		// /a should be indexed
		Assert.True(index.TryGetEntry("/a", out JsonStructuralIndex.IndexEntry a));
		Assert.Equal(JsonValueKind.Object, a.ValueKind);

		// /a/b should be indexed but with ChildCount=-1 (beyond eager limit)
		Assert.True(index.TryGetEntry("/a/b", out JsonStructuralIndex.IndexEntry b));
		Assert.Equal(JsonValueKind.Object, b.ValueKind);
		Assert.Equal(-1, b.ChildCount); // Not eagerly indexed
	}

	[Fact]
	public async Task Build_Indexes_Only_Containers()
	{
		byte[] json = """{"a":1,"b":2,"c":{"d":3}}"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		Assert.True(index.TryGetEntry("", out JsonStructuralIndex.IndexEntry root));
		Assert.Equal(3, root.ChildCount);
		Assert.True(index.TryGetEntry("/c", out _));
		Assert.Equal(2, index.EntryCount);
	}

	[Fact]
	public async Task IndexChildren_Indexes_Deep_Containers_And_Records_The_Count()
	{
		byte[] json = """{"a":{"x":1,"y":{"z":2}}}"""u8.ToArray();

		// Only index depth 0
		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json, 0);

		// /a is indexed but children unknown
		Assert.True(index.TryGetEntry("/a", out JsonStructuralIndex.IndexEntry a));
		Assert.Equal(-1, a.ChildCount);

		Assert.Equal(2, index.IndexChildren("/a", json));

		Assert.True(index.TryGetEntry("/a", out a));
		Assert.Equal(2, a.ChildCount);
		Assert.True(index.TryGetEntry("/a/y", out JsonStructuralIndex.IndexEntry y));
		Assert.Equal(JsonValueKind.Object, y.ValueKind);
		Assert.False(index.TryGetEntry("/a/x", out _));
	}

	[Fact]
	public async Task Repeated_Key_Replaces_An_Earlier_Container()
	{
		byte[] json = """{"a":{"b":{"c":1}},"a":5}"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		Assert.False(index.TryGetEntry("/a", out _));
		Assert.False(index.TryGetEntry("/a/b", out _));
	}

	[Fact]
	public async Task Build_OffsetsCoverValidJson()
	{
		byte[] json = """{"name":"Alice"}"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		Assert.True(index.TryGetEntry("", out JsonStructuralIndex.IndexEntry root));

		// The root entry should span the whole document
		ReadOnlyMemory<byte> slice = json.AsMemory((int)root.StartOffset, (int)(root.EndOffset - root.StartOffset));
		using var doc = JsonDocument.Parse(slice);
		Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
	}

	[Fact]
	public async Task Build_ArrayWithObjects()
	{
		byte[] json = """[{"id":1},{"id":2}]"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		Assert.True(index.TryGetEntry("", out JsonStructuralIndex.IndexEntry root));
		Assert.Equal(JsonValueKind.Array, root.ValueKind);
		Assert.Equal(2, root.ChildCount);

		// Array elements at depth 1
		Assert.True(index.TryGetEntry("/0", out JsonStructuralIndex.IndexEntry first));
		Assert.Equal(JsonValueKind.Object, first.ValueKind);
	}
}
