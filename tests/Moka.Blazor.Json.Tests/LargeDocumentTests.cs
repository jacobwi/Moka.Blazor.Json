using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

// The in-memory source is the reference for what every node should report. The index limits force each way of
// finding a node: a child list, checkpoints, indexed children to jump over, and tokens that straddle blocks.
public sealed class LargeDocumentTests
{
	private static readonly Dictionary<string, JsonIndexLimits> LimitSets = new()
	{
		["default"] = JsonIndexLimits.Default,
		["tiny"] = new JsonIndexLimits
		{
			BlockBytes = 16,
			IndexedContainerBytes = 64,
			AlwaysListedChildren = 2,
			MaxListedChildren = 8,
			MinBytesPerListedChild = 16,
			CheckpointBytes = 32,
			CheckpointChildren = 4
		},
		["checkpoints only"] = new JsonIndexLimits
		{
			BlockBytes = 32,
			IndexedContainerBytes = 128,
			AlwaysListedChildren = 0,
			MaxListedChildren = 0,
			CheckpointBytes = 64,
			CheckpointChildren = 7
		},
		["lists only"] = new JsonIndexLimits
		{
			BlockBytes = 64,
			IndexedContainerBytes = 16,
			AlwaysListedChildren = int.MaxValue,
			MaxListedChildren = int.MaxValue,
			MinBytesPerListedChild = 0,
			CheckpointBytes = 16
		},
		["everything indexed"] = new JsonIndexLimits
		{
			BlockBytes = 8,
			IndexedContainerBytes = 1,
			AlwaysListedChildren = 1,
			MaxListedChildren = 4,
			CheckpointBytes = 1,
			CheckpointChildren = 2
		}
	};

	public static TheoryData<string, bool, bool> Cases()
	{
		var data = new TheoryData<string, bool, bool>();
		foreach (string limits in LimitSets.Keys)
		{
			foreach (bool fromStream in new[] { false, true })
			{
				foreach (bool indented in new[] { false, true })
				{
					data.Add(limits, fromStream, indented);
				}
			}
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public async Task Every_Node_Matches_The_In_Memory_Source(string limits, bool fromStream, bool indented)
	{
		string json = BuildDocument(indented);
		await using JsonDocumentManager expected = await CreateManagerAsync(json);
		await using LazyJsonDocumentSource actual = await CreateLazyAsync(json, limits, fromStream);

		Assert.Equal(expected.CountNodes(), actual.CountNodes());
		Assert.Equal(expected.GetMaxDepth(), actual.GetMaxDepth());
		Assert.Equal(expected.RootValueKind, actual.RootValueKind);

		AssertSameSubtree(expected, actual, "");
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public async Task Streaming_Search_Matches_The_Tree_Search(string limits, bool fromStream, bool indented)
	{
		string json = BuildDocument(indented);
		await using JsonDocumentManager manager = await CreateManagerAsync(json);
		await using LazyJsonDocumentSource lazy = await CreateLazyAsync(json, limits, fromStream);
		var engine = new JsonSearchEngine();

		var searches = new (string Query, JsonSearchOptions Options)[]
		{
			("value 7", new JsonSearchOptions()),
			("VALUE 7", new JsonSearchOptions { CaseSensitive = true }),
			("VALUE 7", new JsonSearchOptions()),
			("deep", new JsonSearchOptions()),
			("deep", new JsonSearchOptions { SearchValues = false }),
			("7", new JsonSearchOptions { SearchKeys = false }),
			("1.5", new JsonSearchOptions()),
			("true", new JsonSearchOptions()),
			("null", new JsonSearchOptions()),
			("é", new JsonSearchOptions()),
			("""line\nbreak""", new JsonSearchOptions { UseRegex = true }),
			("^k1\\d$", new JsonSearchOptions { UseRegex = true }),
			("no such text", new JsonSearchOptions())
		};

		foreach ((string query, JsonSearchOptions options) in searches)
		{
			foreach (string root in new[] { "", "/objects", "/objects/3", "/bigObject" })
			{
				IReadOnlyList<string> expected = engine.FindMatches(manager, root, query, options);

				Assert.True(lazy.TryGetSearchRange(root, out long start, out long end) || root.Length > 0);
				if (end == 0)
				{
					continue;
				}

				JsonSearchResult actual = await lazy.SearchAsync(root, start, end, query, options, int.MaxValue, null,
					CancellationToken.None);

				Assert.Equal(expected, actual.Paths);
				Assert.False(actual.IsTruncated);
			}
		}
	}

	[Fact]
	public async Task Search_Stops_At_The_Match_Limit()
	{
		await using LazyJsonDocumentSource lazy = await CreateLazyAsync(BuildDocument(false), "default", false);

		Assert.True(lazy.TryGetSearchRange("", out long start, out long end));
		JsonSearchResult result = await lazy.SearchAsync("", start, end, "value", new JsonSearchOptions(), 5, null,
			CancellationToken.None);

		Assert.Equal(5, result.Paths.Count);
		Assert.True(result.IsTruncated);
	}

	[Fact]
	public async Task Repeated_Keys_Search_Through_The_Tree()
	{
		await using LazyJsonDocumentSource lazy = await CreateLazyAsync("""{"a":1,"a":2}""", "default", false);

		Assert.False(lazy.TryGetSearchRange("", out _, out _));
	}

	[Theory]
	[InlineData("tiny")]
	[InlineData("checkpoints only")]
	public async Task Repeated_Key_Inside_A_Listed_Child_Counts_Once(string limits)
	{
		const string json = """{"a":{"k":1,"k":2,"m":3},"b":[1,2]}""";
		await using LazyJsonDocumentSource lazy = await CreateLazyAsync(json, limits, true);

		Assert.Equal(2, lazy.GetChildCount("/a"));
		Assert.Equal("2", lazy.GetRawValue("/a/k"));
		Assert.Equal(2, lazy.EnumerateChildren("").First().ChildCount);
	}

	[Fact]
	public async Task Byte_Order_Mark_Is_Skipped()
	{
		byte[] bytes = [0xEF, 0xBB, 0xBF, .."""{"a":[1,2]}"""u8.ToArray()];
		await using LazyJsonDocumentSource lazy = await LazyJsonDocumentSource.CreateAsync(
			new MemoryStream(bytes), true, NullLogger.Instance);

		Assert.Equal(2, lazy.GetChildCount("/a"));
		Assert.Equal("""{"a":[1,2]}""", lazy.GetJsonString(false));
	}

	[Fact]
	public async Task Root_Primitive_Works()
	{
		await using LazyJsonDocumentSource lazy = await CreateLazyAsync("  \"just text\"  ", "default", true);

		Assert.Equal(JsonValueKind.String, lazy.RootValueKind);
		Assert.Equal("just text", lazy.GetRawValue(""));
		Assert.Empty(lazy.EnumerateChildren(""));
		Assert.Equal(0, lazy.GetChildCount(""));
		Assert.Throws<KeyNotFoundException>(() => lazy.GetValueKind("/x"));
	}

	[Theory]
	[InlineData("/missing")]
	[InlineData("/objects/99999")]
	[InlineData("/objects/not-a-number")]
	[InlineData("/name/x")]
	[InlineData("/bigObject/nope")]
	public async Task Missing_Paths_Throw_Key_Not_Found(string path)
	{
		foreach (string limits in LimitSets.Keys)
		{
			await using LazyJsonDocumentSource lazy = await CreateLazyAsync(BuildDocument(false), limits, false);

			Assert.Throws<KeyNotFoundException>(() => lazy.GetValueKind(path));
		}
	}

	[Fact]
	public async Task Values_Too_Large_To_Load_Say_So_Instead_Of_Exhausting_Memory()
	{
		byte[] bytes = Encoding.UTF8.GetBytes(BuildDocument(false));
		await using LazyJsonDocumentSource lazy = await LazyJsonDocumentSource.CreateAsync(
			new MemoryJsonByteSource(bytes), NullLogger.Instance,
			new JsonIndexLimits { IndexedContainerBytes = 64, MaxMaterializedBytes = 256 });

		Assert.Throws<InvalidOperationException>(() => lazy.GetElement(""));
		Assert.Throws<InvalidOperationException>(() => lazy.GetJsonString());

		// Browsing doesn't need to load them.
		Assert.Equal(300, lazy.GetChildCount("/numbers"));
		Assert.Equal("7", lazy.EnumerateChildren("/numbers", 7, 1).Single().RawValue);
	}

	[Fact]
	public async Task A_Caller_Stream_Stays_Open_And_An_Owned_Stream_Closes()
	{
		var callerStream = new MemoryStream("""{"a":1}"""u8.ToArray());
		LazyJsonDocumentSource borrowed = await LazyJsonDocumentSource.CreateAsync(callerStream, false, NullLogger.Instance);
		await borrowed.DisposeAsync();
		Assert.True(callerStream.CanRead);

		var ownedStream = new MemoryStream("""{"a":1}"""u8.ToArray());
		LazyJsonDocumentSource owned = await LazyJsonDocumentSource.CreateAsync(ownedStream, true, NullLogger.Instance);
		await owned.DisposeAsync();
		Assert.False(ownedStream.CanRead);
	}

	[Fact]
	public async Task A_Failed_Index_Closes_An_Owned_Stream()
	{
		var stream = new MemoryStream("""{"a":"""u8.ToArray());

		await Assert.ThrowsAnyAsync<JsonException>(() =>
			LazyJsonDocumentSource.CreateAsync(stream, true, NullLogger.Instance));

		Assert.False(stream.CanRead);
	}

	[Fact]
	public async Task Reads_Are_Safe_From_Many_Threads()
	{
		string json = BuildDocument(false);
		await using LazyJsonDocumentSource lazy = await CreateLazyAsync(json, "tiny", true);
		await using JsonDocumentManager manager = await CreateManagerAsync(json);

		var expected = manager.EnumerateChildren("/objects").ToList();

		await Parallel.ForEachAsync(Enumerable.Range(0, 64), async (i, _) =>
		{
			await Task.Yield();
			Assert.Equal(expected, lazy.EnumerateChildren("/objects", 0, int.MaxValue - i));
			Assert.Equal(manager.GetRawValue("/numbers/" + i), lazy.GetRawValue("/numbers/" + i));
		});
	}

	#region Spooling forward-only streams

	[Fact]
	public async Task Spool_Stays_In_Memory_Under_The_Limit()
	{
		await using Stream stream = await JsonStreamSpooler.ToSeekableAsync(
			new ForwardOnlyStream("""{"a":1}"""u8.ToArray()), 1024, long.MaxValue);

		Assert.IsType<MemoryStream>(stream);
		Assert.Equal(0, stream.Position);
		Assert.Equal(7, stream.Length);
	}

	[Fact]
	public async Task Spool_Moves_To_A_Temp_File_Over_The_Limit_And_Deletes_It_On_Close()
	{
		byte[] bytes = Encoding.UTF8.GetBytes(BuildDocument(false));
		string path;

		await using (Stream stream = await JsonStreamSpooler.ToSeekableAsync(new ForwardOnlyStream(bytes), 64, long.MaxValue))
		{
			FileStream file = Assert.IsType<FileStream>(stream);
			path = file.Name;
			Assert.True(File.Exists(path));
			Assert.Equal(bytes.Length, stream.Length);

			byte[] copy = new byte[bytes.Length];
			await stream.ReadExactlyAsync(copy);
			Assert.Equal(bytes, copy);
		}

		Assert.False(File.Exists(path));
	}

	[Fact]
	public async Task Spool_Enforces_The_Size_Limit()
	{
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			JsonStreamSpooler.ToSeekableAsync(new ForwardOnlyStream(new byte[4096]), 64, 1024));
	}

	#endregion

	#region Helpers

	private static string BuildDocument(bool indented)
	{
		var sb = new StringBuilder("{");
		sb.Append("""
		          "name":"root","nothing":null,"yes":true,"no":false,"pi":1.5e3,"emptyObject":{},"emptyArray":[],
		          "esc\"aped/key~1":{"a\nb":"line\nbreak","café":"ünï 🚀","":"empty key"},
		          // a comment, which lazy parsing has to skip
		          "numbers":[
		          """);
		sb.AppendJoin(',', Enumerable.Range(0, 300));
		sb.Append("""],"strings":[""");
		sb.AppendJoin(',', Enumerable.Range(0, 120).Select(i => $"\"value {i}\""));
		sb.Append("""],"objects":[""");
		sb.AppendJoin(',', Enumerable.Range(0, 60).Select(i =>
			"""{"id":ID,"tags":["x","y"],"nested":{"deep":{"deeper":[1,2,{"z":null}],"n":DOUBLE}}}"""
				.Replace("ID", i.ToString(CultureInfo.InvariantCulture))
				.Replace("DOUBLE", (i * 2).ToString(CultureInfo.InvariantCulture))));
		sb.Append("""],"mixed":[1,"two",true,null,{"five":5},[6,7],{"big":[""");
		sb.AppendJoin(',', Enumerable.Range(0, 200));
		sb.Append("""]},"after big",9,],"bigObject":{""");
		sb.AppendJoin(',', Enumerable.Range(0, 150).Select(i => i % 10 == 0
			? $"\"k{i}\":{{\"inner\":[{i},{i + 1}]}}"
			: $"\"k{i}\":\"value {i}\""));
		sb.Append("""},"chain":""");
		sb.Append(string.Concat(Enumerable.Repeat("""{"down":[""", 20)));
		sb.Append('0');
		sb.Append(string.Concat(Enumerable.Repeat("]}", 20)));
		sb.Append(",}");

		if (!indented)
		{
			return sb.ToString();
		}

		using var doc = JsonDocument.Parse(sb.ToString(), new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
			MaxDepth = 256
		});
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			doc.RootElement.WriteTo(writer);
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static void AssertSameSubtree(IJsonDocumentSource expected, IJsonDocumentSource actual, string path)
	{
		Assert.Equal(expected.GetValueKind(path), actual.GetValueKind(path));
		Assert.Equal(expected.GetChildCount(path), actual.GetChildCount(path));
		Assert.Equal(expected.GetRawValue(path), actual.GetRawValue(path));
		Assert.Equal(expected.GetByteLength(path), actual.GetByteLength(path));
		Assert.Equal(expected.GetElement(path).GetRawText(), actual.GetElement(path).GetRawText());

		var children = expected.EnumerateChildren(path).ToList();
		Assert.Equal(children, actual.EnumerateChildren(path));
		Assert.Equal(expected.EnumerateChildren(path, 1, 3), actual.EnumerateChildren(path, 1, 3));
		Assert.Equal(expected.EnumerateChildren(path, children.Count, 3), actual.EnumerateChildren(path, children.Count, 3));

		for (int i = 0; i < children.Count; i++)
		{
			string segment = children[i].Path[(path.Length + 1)..];
			Assert.Equal(i, actual.GetChildIndex(path, segment));

			if (children[i].ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				AssertSameSubtree(expected, actual, children[i].Path);
			}
			else
			{
				Assert.Equal(children[i].RawValue, actual.GetRawValue(children[i].Path));
				Assert.Equal(expected.GetByteLength(children[i].Path), actual.GetByteLength(children[i].Path));
			}
		}
	}

	private static async Task<JsonDocumentManager> CreateManagerAsync(string json)
	{
		var manager = new JsonDocumentManager(NullLogger<JsonDocumentManager>.Instance,
			Options.Create(new MokaJsonViewerOptions()));
		await manager.ParseAsync(json);
		return manager;
	}

	private static Task<LazyJsonDocumentSource> CreateLazyAsync(string json, string limits, bool fromStream)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(json);
		return fromStream
			? LazyJsonDocumentSource.CreateAsync(new MemoryStream(bytes), true, NullLogger.Instance, LimitSets[limits])
			: LazyJsonDocumentSource.CreateAsync(new MemoryJsonByteSource(bytes), NullLogger.Instance, LimitSets[limits]);
	}

	private sealed class ForwardOnlyStream(byte[] bytes) : Stream
	{
		private int _position;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int read = Math.Min(Math.Min(count, 100), bytes.Length - _position);
			Buffer.BlockCopy(bytes, _position, buffer, offset, read);
			_position += read;
			return read;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	#endregion
}
