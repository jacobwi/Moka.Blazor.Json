using System.Text;
using System.Text.Json;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonStructuralIndexTests
{
	private static readonly JsonIndexLimits Tiny = new()
	{
		BlockBytes = 16,
		IndexedContainerBytes = 32,
		AlwaysListedChildren = 2,
		MaxListedChildren = 8,
		MinBytesPerListedChild = 8,
		CheckpointBytes = 32,
		CheckpointChildren = 4
	};

	private static Task<JsonStructuralIndex> BuildAsync(string json, JsonIndexLimits? limits = null) =>
		JsonStructuralIndex.BuildAsync(new MemoryJsonByteSource(Encoding.UTF8.GetBytes(json)), 0, limits);

	[Fact]
	public async Task Root_Is_Always_Indexed_With_Its_Children()
	{
		JsonStructuralIndex index = await BuildAsync("""{"a":1,"b":"hello","c":true}""");

		Assert.NotNull(index.Root);
		Assert.Equal(JsonValueKind.Object, index.Root.Kind);
		Assert.Equal(3, index.Root.ChildCount);
		Assert.Equal(["a", "b", "c"], index.Root.Children!.Select(c => c.Name));
		Assert.Equal(
			[JsonValueKind.Number, JsonValueKind.String, JsonValueKind.True],
			index.Root.Children!.Select(c => c.Kind));
	}

	[Fact]
	public async Task Root_Primitive_Has_No_Container()
	{
		JsonStructuralIndex index = await BuildAsync(" 42 ");

		Assert.Null(index.Root);
		Assert.Equal(JsonValueKind.Number, index.RootValue.Kind);
		Assert.Equal(1, index.RootValue.Start);
		Assert.Equal(3, index.RootValue.End);
	}

	[Fact]
	public async Task Counts_Nodes_And_Depth_Across_The_Whole_Document()
	{
		JsonStructuralIndex index = await BuildAsync("""{"a":[1,{"b":null}],"c":2}""");

		Assert.Equal(6, index.NodeCount);
		Assert.Equal(3, index.MaxDepth);
	}

	[Fact]
	public async Task Small_Containers_Are_Not_Indexed()
	{
		JsonStructuralIndex index = await BuildAsync("""{"user":{"name":"Alice"},"count":1}""");

		Assert.Equal(1, index.EntryCount);
		IndexedChild user = index.Root!.Children![0];
		Assert.Equal(1, user.ChildCount);
		Assert.False(index.TryGetContainer(user.Start, out _));
	}

	[Fact]
	public async Task Containers_Over_The_Limit_Are_Indexed_At_Any_Depth()
	{
		string big = new('x', 64);
		JsonStructuralIndex index = await BuildAsync("""{"a":{"b":{"c":"BIG"}},"small":{"d":1}}""".Replace("BIG", big),
			new JsonIndexLimits { IndexedContainerBytes = 32 });

		IndexedChild a = index.Root!.Children![0];
		Assert.True(index.TryGetContainer(a.Start, out IndexedContainer? indexedA));
		IndexedChild b = indexedA.Children![0];
		Assert.True(index.TryGetContainer(b.Start, out _));

		IndexedChild small = index.Root.Children[1];
		Assert.False(index.TryGetContainer(small.Start, out _));
	}

	[Fact]
	public async Task Many_Small_Children_Use_Checkpoints_Instead_Of_A_List()
	{
		string numbers = string.Join(',', Enumerable.Range(0, 100));
		JsonStructuralIndex index = await BuildAsync($"[{numbers}]", Tiny);

		Assert.Null(index.Root!.Children);
		Assert.Equal(100, index.Root.ChildCount);
		Assert.Equal(0, index.Root.CheckpointIndexes[0]);
		Assert.All(index.Root.CheckpointIndexes.Zip(index.Root.CheckpointIndexes.Skip(1)),
			pair => Assert.True(pair.Second - pair.First <= Tiny.CheckpointChildren));
	}

	[Fact]
	public async Task Child_After_An_Indexed_Child_Always_Has_A_Checkpoint()
	{
		string big = string.Join(',', Enumerable.Range(0, 50).Select(i => $"\"{i}\""));
		string small = string.Join(',', Enumerable.Range(0, 20));
		JsonStructuralIndex index = await BuildAsync($"[{small},[{big}],{small}]", Tiny);

		Assert.Null(index.Root!.Children);
		Assert.Contains(21, index.Root.CheckpointIndexes);
	}

	[Fact]
	public async Task Offsets_Point_At_The_Value()
	{
		const string json = """{  "name" : "Alice" , "list" : [1, 2]  }""";
		JsonStructuralIndex index = await BuildAsync(json);

		IndexedChild name = index.Root!.Children![0];
		IndexedChild list = index.Root.Children[1];

		Assert.Equal("\"Alice\"", json[(int)name.Start..(int)name.End]);
		Assert.Equal("[1, 2]", json[(int)list.Start..(int)list.End]);
		Assert.Equal(json.Length, (int)index.Root.End);
	}

	[Fact]
	public async Task Escaped_Property_Names_Are_Decoded()
	{
		JsonStructuralIndex index = await BuildAsync("""{"a\"b":1,"café":2}""");

		Assert.Equal(["a\"b", "café"], index.Root!.Children!.Select(c => c.Name));
		Assert.False(index.MayHaveDuplicateKeys);
	}

	[Theory]
	[InlineData("""{"a":1,"b":{"a":2},"c":[{"a":1,"b":2}]}""", false)]
	[InlineData("""{"a":1,"b":2,"a":3}""", true)]
	[InlineData("""[{"x":1},{"y":{"k":1,"k":2}}]""", true)]
	[InlineData("""{"a":1,"a":2}""", true)]
	public async Task Detects_Repeated_Keys(string json, bool expected)
	{
		JsonStructuralIndex index = await BuildAsync(json);

		Assert.Equal(expected, index.MayHaveDuplicateKeys);
	}

	[Fact]
	public async Task Detects_A_Repeated_Key_In_An_Object_With_Many_Keys()
	{
		string keys = string.Join(',', Enumerable.Range(0, 40).Select(i => $"\"k{i}\":{i}"));

		Assert.False((await BuildAsync($"{{{keys}}}")).MayHaveDuplicateKeys);
		Assert.True((await BuildAsync($"{{{keys},\"k30\":0}}")).MayHaveDuplicateKeys);
	}

	[Fact]
	public async Task A_Repeated_Key_Keeps_Its_First_Position_And_Last_Value()
	{
		JsonStructuralIndex index = await BuildAsync("""{"a":{"b":{"c":1}},"x":1,"a":5}""");

		Assert.Equal(2, index.Root!.ChildCount);
		Assert.Equal(["a", "x"], index.Root.Children!.Select(c => c.Name));
		Assert.Equal(JsonValueKind.Number, index.Root.Children![0].Kind);
	}

	[Fact]
	public async Task A_Token_Larger_Than_A_Block_Still_Fits()
	{
		string text = new('z', 5000);
		JsonStructuralIndex index = await BuildAsync($$"""{"long":"{{text}}","next":1}""",
			new JsonIndexLimits { BlockBytes = 64 });

		Assert.Equal(2, index.Root!.ChildCount);
		Assert.Equal(text.Length + 2, index.Root.Children![0].End - index.Root.Children[0].Start);
	}

	[Theory]
	[InlineData("""{"a":1""")]
	[InlineData("""{"a":}""")]
	[InlineData("""[1,2] 3""")]
	[InlineData("")]
	public async Task Invalid_Json_Throws(string json)
	{
		// Thrown as a subtype of JsonException, which Assert.ThrowsAsync wouldn't accept.
		await Assert.ThrowsAnyAsync<JsonException>(() => BuildAsync(json, new JsonIndexLimits { BlockBytes = 4 }));
	}

	[Fact]
	public async Task Cancellation_Stops_The_Scan()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			JsonStructuralIndex.BuildAsync(new MemoryJsonByteSource("[1,2,3]"u8.ToArray()),
				cancellationToken: cts.Token));
	}

	[Fact]
	public async Task Reports_Progress_Between_Blocks()
	{
		var offsets = new List<long>();
		string numbers = string.Join(',', Enumerable.Range(0, 200));

		await JsonStructuralIndex.BuildAsync(new MemoryJsonByteSource(Encoding.UTF8.GetBytes($"[{numbers}]")), 0,
			new JsonIndexLimits { BlockBytes = 64 }, offsets.Add);

		Assert.NotEmpty(offsets);
		Assert.Equal(offsets.Order(), offsets);
	}
}
