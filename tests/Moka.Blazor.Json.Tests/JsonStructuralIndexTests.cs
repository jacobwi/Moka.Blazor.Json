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

		// /count should be indexed as a primitive
		Assert.True(index.TryGetEntry("/count", out JsonStructuralIndex.IndexEntry count));
		Assert.Equal(JsonValueKind.Number, count.ValueKind);
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
	public async Task GetDirectChildren_ReturnsOnlyDirectChildren()
	{
		byte[] json = """{"a":1,"b":2,"c":{"d":3}}"""u8.ToArray();

		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json);

		var children = index.GetDirectChildren("").ToList();

		// Should get /a, /b, /c (3 direct children)
		Assert.Equal(3, children.Count);
	}

	[Fact]
	public async Task IndexChildrenAsync_LazilyIndexesDeepContainers()
	{
		byte[] json = """{"a":{"x":1,"y":2}}"""u8.ToArray();

		// Only index depth 0
		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(json, 0);

		// /a is indexed but children unknown
		Assert.True(index.TryGetEntry("/a", out JsonStructuralIndex.IndexEntry a));
		Assert.Equal(-1, a.ChildCount);

		// Lazy-index children of /a
		await index.IndexChildrenAsync("/a", json);

		// Now children should be available
		var children = index.GetDirectChildren("/a").ToList();
		Assert.Equal(2, children.Count);
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
