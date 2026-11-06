using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

// For documents too large to parse whole. Only the structural index stays in memory. Everything else is read from
// the document's bytes on demand, so the document can be far larger than memory or a single array.
internal sealed class LazyJsonDocumentSource : IJsonDocumentSource
{
	private const int SubtreeCacheEntries = 64;
	private const long LargestCachedSubtreeBytes = 64 * 1024 * 1024;
	private const int ChildListCacheEntries = 16 * 1024;
	private const long ChildListCacheRows = 1_000_000;
	private const int WalkBlockBytes = 256 * 1024;

	private readonly IJsonByteSource _bytes;
	private readonly Lock _cacheLock = new();
	private readonly LruCache<ChildListKey, IReadOnlyList<JsonChildDescriptor>> _childLists;
	private readonly JsonStructuralIndex _index;
	private readonly JsonIndexLimits _limits;
	private readonly LruCache<long, ParsedSubtree> _subtrees;
	private bool _disposed;

	private LazyJsonDocumentSource(IJsonByteSource bytes, JsonStructuralIndex index, JsonIndexLimits limits,
		TimeSpan parseTime)
	{
		_bytes = bytes;
		_index = index;
		_limits = limits;
		ParseTime = parseTime;

		// Callers on other threads may still be reading an element from an evicted document
		// (search and flatten run concurrently), so eviction leaves disposal to the GC.
		_subtrees = new LruCache<long, ParsedSubtree>(SubtreeCacheEntries, disposeOnEvict: false);
		_childLists = new LruCache<ChildListKey, IReadOnlyList<JsonChildDescriptor>>(ChildListCacheEntries,
			disposeOnEvict: false, weigh: list => list.Count, maxWeight: ChildListCacheRows);

		DebugStats = new LazyDebugStats
		{
			TotalBytes = bytes.Length,
			IndexEntries = index.EntryCount
		};
	}

	internal LazyDebugStats DebugStats { get; }

	// Takes ownership of bytes, which it reads from for as long as it lives.
	public static async Task<LazyJsonDocumentSource> CreateAsync(
		IJsonByteSource bytes,
		ILogger logger,
		JsonIndexLimits? limits = null,
		Action<long>? progress = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			limits ??= JsonIndexLimits.Default;
			var sw = Stopwatch.StartNew();
			long start = HasByteOrderMark(bytes) ? 3 : 0;

			JsonStructuralIndex index = await Task.Run(
				() => JsonStructuralIndex.BuildAsync(bytes, start, limits, progress, cancellationToken),
				cancellationToken).ConfigureAwait(false);
			sw.Stop();

			logger.LogDebug("Built lazy JSON index: {Size} bytes, {Entries} entries, {Time}ms",
				bytes.Length, index.EntryCount, sw.Elapsed.TotalMilliseconds);

			return new LazyJsonDocumentSource(bytes, index, limits, sw.Elapsed);
		}
		catch
		{
			bytes.Dispose();
			throw;
		}
	}

	public static Task<LazyJsonDocumentSource> CreateAsync(
		Stream stream,
		bool ownsStream,
		ILogger logger,
		JsonIndexLimits? limits = null,
		Action<long>? progress = null,
		CancellationToken cancellationToken = default) =>
		CreateAsync(new StreamJsonByteSource(stream, ownsStream), logger, limits, progress, cancellationToken);

	public static async Task<LazyJsonDocumentSource> CreateFromStringAsync(
		string json,
		ILogger logger,
		MokaJsonViewerOptions options,
		JsonIndexLimits? limits = null,
		CancellationToken cancellationToken = default)
	{
		byte[] buffer = await Task.Run(() => Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
		return await CreateAsync(new MemoryJsonByteSource(buffer), logger, limits, null, cancellationToken)
			.ConfigureAwait(false);
	}

	private static bool HasByteOrderMark(IJsonByteSource bytes)
	{
		if (bytes.Length < 3)
		{
			return false;
		}

		Span<byte> head = stackalloc byte[3];
		bytes.Read(0, head);
		return head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
	}

	#region Search

	// False if the search has to go through the tree instead: repeated keys need resolving, and only nodes the
	// index knows about have a known range.
	public bool TryGetSearchRange(string path, out long start, out long end)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		start = end = 0;
		if (_index.MayHaveDuplicateKeys)
		{
			return false;
		}

		Location at = Locate(path);
		if (at.Container is { } container)
		{
			(start, end) = (container.Start, container.End);
			return true;
		}

		if (at.Relative.Length == 0)
		{
			(start, end) = (at.Child.Start, at.Child.End);
			return true;
		}

		return false;
	}

	public Task<JsonSearchResult> SearchAsync(
		string rootPath,
		long start,
		long end,
		string query,
		JsonSearchOptions options,
		int maxMatches,
		IProgress<StreamingJsonSearcher.SearchProgress>? progress,
		CancellationToken cancellationToken) =>
		StreamingJsonSearcher.SearchAsync(_bytes, start, end, rootPath, query, options, maxMatches, progress,
			cancellationToken);

	#endregion

	#region IJsonDocumentSource

	public bool IsLoaded => true;
	public long DocumentSizeBytes => _bytes.Length;
	public TimeSpan ParseTime { get; }
	public JsonValueKind RootValueKind => _index.RootValue.Kind;
	public bool SupportsEditing => false;

	public int GetChildCount(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		Location at = Locate(path);
		if (at.Container is { } container)
		{
			return container.ChildCount;
		}

		if (at.Relative.Length == 0)
		{
			return ChildCountOf(at.Child);
		}

		return JsonObjectReader.CountChildren(ElementAt(at, path), _index.MayHaveDuplicateKeys);
	}

	public IEnumerable<JsonChildDescriptor> EnumerateChildren(string path) => ReadChildren(path, 0, int.MaxValue);

	public IEnumerable<JsonChildDescriptor> EnumerateChildren(string path, int start, int count) =>
		ReadChildren(path, start, count);

	public int GetChildIndex(string path, string escapedSegment)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		string segment = JsonPointerHelper.UnescapeSegment(escapedSegment);
		Location at = Locate(path);

		if (at.Container is { } container)
		{
			if (container.Kind == JsonValueKind.Array)
			{
				return ParseIndex(segment, container.ChildCount);
			}

			if (container.Children is not null)
			{
				return container.IndexOfName(segment);
			}

			int position = -1;
			WalkChildren(container, 0, (index, child, _) =>
			{
				if (!string.Equals(child.Name, segment, StringComparison.Ordinal))
				{
					return true;
				}

				position = index;
				return false;
			});
			return position;
		}

		JsonElement element = ElementAt(at, path);
		if (element.ValueKind == JsonValueKind.Array)
		{
			return ParseIndex(segment, element.GetArrayLength());
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			int i = 0;
			foreach (KeyValuePair<string, JsonElement> property in EnumerateProperties(element))
			{
				if (string.Equals(property.Key, segment, StringComparison.Ordinal))
				{
					return i;
				}

				i++;
			}
		}

		return -1;
	}

	public string? GetRawValue(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		Location at = Locate(path);
		if (at.Container is not null)
		{
			return null;
		}

		if (at.Relative.Length == 0)
		{
			return at.Child.IsContainer ? null : ReadPrimitive(at.Child);
		}

		JsonElement element = ElementAt(at, path);
		return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
			? null
			: GetPrimitiveRawValue(element);
	}

	public JsonValueKind GetValueKind(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		Location at = Locate(path);
		if (at.Container is { } container)
		{
			return container.Kind;
		}

		return at.Relative.Length == 0 ? at.Child.Kind : ElementAt(at, path).ValueKind;
	}

	public long GetByteLength(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		Location at = Locate(path);
		if (at.Container is { } container)
		{
			return container.End - container.Start;
		}

		return at.Relative.Length == 0
			? at.Child.End - at.Child.Start
			: JsonMarshal.GetRawUtf8Value(ElementAt(at, path)).Length;
	}

	public JsonElement GetElement(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		Location at = Locate(path);
		if (at.Container is { } container)
		{
			return Parse(container.Start, container.End, path).Document.RootElement;
		}

		return ElementAt(at, path);
	}

	public string GetJsonString(bool indented = true)
	{
		using var stream = new MemoryStream();
		WriteJson(stream, indented);
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	public void WriteJson(Stream destination, bool indented = true)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		// Re-write even when not indented: the raw bytes keep the source's own formatting,
		// comments and trailing commas, so they are neither minified nor strict JSON.
		IndexedChild root = _index.RootValue;
		EnsureCanMaterialize(root.End - root.Start);
		using var doc = JsonDocument.Parse(_bytes.GetMemory(root.Start, (int)(root.End - root.Start)),
			JsonFormatting.LenientDocument);
		using var writer = new Utf8JsonWriter(destination, JsonFormatting.Writer(indented));
		doc.RootElement.WriteTo(writer);
	}

	public long CountNodes() => _index.NodeCount;

	public int GetMaxDepth() => _index.MaxDepth;

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		lock (_cacheLock)
		{
			_subtrees.Clear();
			_childLists.Clear();
		}

		_bytes.Dispose();
		return ValueTask.CompletedTask;
	}

	#endregion

	#region Finding nodes

	// Where a path leads: an indexed container, or a child of one plus what's left of the path inside that child.
	private readonly record struct Location(IndexedContainer? Container, IndexedChild Child, string Relative);

	private Location Locate(string path)
	{
		IndexedContainer? current = _index.Root;
		if (current is null)
		{
			return new Location(null, _index.RootValue, path);
		}

		if (path.Length == 0)
		{
			return new Location(current, default, "");
		}

		int position = path[0] == '/' ? 0 : -1;
		while (position < path.Length)
		{
			int next = path.IndexOf('/', position + 1);
			if (next < 0)
			{
				next = path.Length;
			}

			string segment = JsonPointerHelper.UnescapeSegment(path[(position + 1)..next]);
			IndexedChild child = FindChild(current, segment, path);

			if (!child.IsContainer || !_index.TryGetContainer(child.Start, out IndexedContainer? nested))
			{
				return new Location(null, child, path[next..]);
			}

			current = nested;
			position = next;
		}

		return new Location(current, default, "");
	}

	private IndexedChild FindChild(IndexedContainer container, string segment, string path)
	{
		if (container.Kind == JsonValueKind.Array)
		{
			int index = ParseIndex(segment, container.ChildCount);
			if (index < 0)
			{
				throw new KeyNotFoundException($"Invalid array index '{segment}' at path '{path}'.");
			}

			if (container.Children is { } elements)
			{
				return elements[index];
			}
		}
		else if (container.Children is { } properties)
		{
			int position = container.IndexOfName(segment);
			return position >= 0
				? properties[position]
				: throw new KeyNotFoundException($"Property '{segment}' not found at path '{path}'.");
		}

		IndexedChild found = default;
		bool isFound = false;
		int firstIndex = container.Kind == JsonValueKind.Array ? ParseIndex(segment, container.ChildCount) : 0;
		WalkChildren(container, firstIndex, (index, child, _) =>
		{
			if (container.Kind == JsonValueKind.Object && !string.Equals(child.Name, segment, StringComparison.Ordinal))
			{
				return true;
			}

			found = child;
			isFound = true;
			return false;
		});

		return isFound ? found : throw new KeyNotFoundException($"Path '{path}' not found.");
	}

	private static int ParseIndex(string segment, int count) =>
		int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < count
			? index
			: -1;

	private JsonElement ElementAt(Location at, string path)
	{
		ParsedSubtree subtree = Parse(at.Child.Start, at.Child.End, path);
		return at.Relative.Length == 0
			? subtree.Document.RootElement
			: Navigate(subtree, path, path.Length - at.Relative.Length);
	}

	// A step into an array or object is linear in its position, so containers are remembered and lookups start
	// from the nearest one.
	private static JsonElement Navigate(ParsedSubtree subtree, string path, int subtreePathLength)
	{
		if (subtree.Containers.TryGetValue(path, out JsonElement cached))
		{
			return cached;
		}

		JsonElement current = subtree.Document.RootElement;
		int resolved = subtreePathLength;
		for (int slash = path.LastIndexOf('/');
		     slash > subtreePathLength;
		     slash = path.LastIndexOf('/', slash - 1))
		{
			if (subtree.Containers.TryGetValue(path[..slash], out JsonElement ancestor))
			{
				current = ancestor;
				resolved = slash;
				break;
			}
		}

		foreach (string segment in JsonPointerHelper.Split(path[resolved..]))
		{
			string unescaped = JsonPointerHelper.UnescapeSegment(segment);

			if (current.ValueKind == JsonValueKind.Object)
			{
				if (!current.TryGetProperty(unescaped, out JsonElement next))
				{
					throw new KeyNotFoundException($"Property '{unescaped}' not found.");
				}

				current = next;
			}
			else if (current.ValueKind == JsonValueKind.Array)
			{
				if (!int.TryParse(unescaped, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
				    index >= current.GetArrayLength())
				{
					throw new KeyNotFoundException($"Invalid array index '{unescaped}'.");
				}

				current = current[index];
			}
			else
			{
				throw new KeyNotFoundException($"Cannot navigate into {current.ValueKind}.");
			}
		}

		if (current.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
		{
			subtree.Containers.TryAdd(path, current);
		}

		return current;
	}

	// Parsing happens outside the cache lock so a cached lookup (often on the UI thread) never waits for
	// another thread's parse. Two threads may parse the same subtree at once; the first one cached wins.
	private ParsedSubtree Parse(long start, long end, string path)
	{
		lock (_cacheLock)
		{
			if (_subtrees.TryGet(start, out ParsedSubtree? cached))
			{
				DebugStats.RecordCacheHit();
				return cached;
			}
		}

		DebugStats.RecordCacheMiss();
		long length = end - start;
		EnsureCanMaterialize(length);

		var sw = Stopwatch.StartNew();
		var parsed = new ParsedSubtree(
			JsonDocument.Parse(_bytes.GetMemory(start, (int)length), JsonFormatting.LenientDocument));
		sw.Stop();
		DebugStats.RecordParse(path.Length == 0 ? "(root)" : path, start, length, sw.Elapsed);

		lock (_cacheLock)
		{
			if (_disposed)
			{
				parsed.Document.Dispose();
				throw new ObjectDisposedException(nameof(LazyJsonDocumentSource));
			}

			if (_subtrees.TryGet(start, out ParsedSubtree? raced))
			{
				parsed.Document.Dispose();
				return raced;
			}

			if (length <= LargestCachedSubtreeBytes)
			{
				_subtrees.Set(start, parsed);
			}
		}

		return parsed;
	}

	private void EnsureCanMaterialize(long length)
	{
		if (length > _limits.MaxMaterializedBytes)
		{
			throw new InvalidOperationException(
				$"This value is too large to load at once ({JsonDocumentManager.FormatBytes(length)}).");
		}
	}

	#endregion

	#region Reading children

	private IReadOnlyList<JsonChildDescriptor> ReadChildren(string path, int start, int count)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		var key = new ChildListKey(path, start, count);
		lock (_cacheLock)
		{
			if (_childLists.TryGet(key, out IReadOnlyList<JsonChildDescriptor>? cached))
			{
				return cached;
			}
		}

		Location at = Locate(path);
		List<JsonChildDescriptor> children;
		if (at.Container is { Children: not null } listed)
		{
			children = ChildrenFromList(listed, path, start, count);
		}
		else if (at.Container is { } unlisted)
		{
			children = ChildrenFromBytes(unlisted, path, start, count);
		}
		else if (at.Relative.Length == 0 && !at.Child.IsContainer)
		{
			return [];
		}
		else
		{
			ParsedSubtree subtree = Parse(at.Child.Start, at.Child.End, path);
			JsonElement element = at.Relative.Length == 0
				? subtree.Document.RootElement
				: Navigate(subtree, path, path.Length - at.Relative.Length);
			children = ChildrenFromElement(subtree, element, path, start, count);
		}

		lock (_cacheLock)
		{
			if (!_disposed)
			{
				_childLists.Set(key, children);
			}
		}

		return children;
	}

	private List<JsonChildDescriptor> ChildrenFromList(IndexedContainer container, string path, int start, int count)
	{
		IndexedChild[] children = container.Children!;
		int end = (int)Math.Min((long)start + count, children.Length);
		var result = new List<JsonChildDescriptor>(Math.Max(0, end - start));

		for (int i = start; i < end; i++)
		{
			IndexedChild child = children[i];
			result.Add(Describe(child, path, i, child.IsContainer ? null : ReadPrimitive(child)));
		}

		return result;
	}

	private List<JsonChildDescriptor> ChildrenFromBytes(IndexedContainer container, string path, int start, int count)
	{
		long wanted = Math.Min(count, (long)container.ChildCount - start);
		var result = new List<JsonChildDescriptor>((int)Math.Clamp(wanted, 0, int.MaxValue));
		if (wanted <= 0)
		{
			return result;
		}

		long last = start + wanted - 1;
		WalkChildren(container, start, (index, child, rawValue) =>
		{
			result.Add(Describe(child, path, index, rawValue));
			return index < last;
		});

		return result;
	}

	private List<JsonChildDescriptor> ChildrenFromElement(ParsedSubtree subtree, JsonElement element, string path,
		int start, int count)
	{
		var result = new List<JsonChildDescriptor>();
		long end = (long)start + count;
		bool mayHaveDuplicateKeys = _index.MayHaveDuplicateKeys;

		if (element.ValueKind == JsonValueKind.Object)
		{
			int i = 0;
			foreach (KeyValuePair<string, JsonElement> prop in EnumerateProperties(element))
			{
				if (i >= end)
				{
					break;
				}

				if (i >= start)
				{
					string childPath = $"{path}/{JsonPointerHelper.EscapeSegment(prop.Key)}";
					result.Add(DescribeElement(subtree, prop.Value, childPath, prop.Key, null, mayHaveDuplicateKeys));
				}

				i++;
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			int i = 0;
			foreach (JsonElement item in element.EnumerateArray())
			{
				if (i >= end)
				{
					break;
				}

				if (i >= start)
				{
					string childPath = string.Create(CultureInfo.InvariantCulture, $"{path}/{i}");
					result.Add(DescribeElement(subtree, item, childPath, null, i, mayHaveDuplicateKeys));
				}

				i++;
			}
		}

		return result;
	}

	private static JsonChildDescriptor DescribeElement(ParsedSubtree subtree, JsonElement value, string childPath,
		string? name, int? index, bool mayHaveDuplicateKeys)
	{
		bool isContainer = value.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
		if (isContainer)
		{
			subtree.Containers.TryAdd(childPath, value);
		}

		return new JsonChildDescriptor(childPath, name, index, value.ValueKind,
			isContainer ? null : GetPrimitiveRawValue(value),
			JsonObjectReader.CountChildren(value, mayHaveDuplicateKeys));
	}

	private JsonChildDescriptor Describe(IndexedChild child, string parentPath, int index, string? rawValue)
	{
		string childPath = child.Name is null
			? string.Create(CultureInfo.InvariantCulture, $"{parentPath}/{index}")
			: $"{parentPath}/{JsonPointerHelper.EscapeSegment(child.Name)}";

		return new JsonChildDescriptor(childPath, child.Name, child.Name is null ? index : null, child.Kind, rawValue,
			ChildCountOf(child));
	}

	// The index counts every key, so a container with a repeated key would count it twice.
	private int ChildCountOf(IndexedChild child)
	{
		if (!child.IsContainer)
		{
			return 0;
		}

		if (_index.TryGetContainer(child.Start, out IndexedContainer? indexed))
		{
			return indexed.ChildCount;
		}

		return _index.MayHaveDuplicateKeys
			? JsonObjectReader.CountChildren(Parse(child.Start, child.End, child.Name ?? "").Document.RootElement, true)
			: child.ChildCount;
	}

	private string? ReadPrimitive(IndexedChild child)
	{
		ReadOnlySpan<byte> bytes = _bytes.GetMemory(child.Start, (int)(child.End - child.Start)).Span;
		var reader = new Utf8JsonReader(bytes, JsonFormatting.LenientReader);
		reader.Read();
		return ReadPrimitive(ref reader);
	}

	private static string? ReadPrimitive(ref Utf8JsonReader reader) => reader.TokenType switch
	{
		JsonTokenType.String => reader.GetString(),
		JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
		JsonTokenType.True => "true",
		JsonTokenType.False => "false",
		JsonTokenType.Null => "null",
		_ => null
	};

	private delegate bool ChildVisitor(int index, IndexedChild child, string? rawValue);

	// Costs about as much as reading those children, plus up to one checkpoint gap before them.
	private void WalkChildren(IndexedContainer container, int firstIndex, ChildVisitor visit)
	{
		int next = firstIndex;
		while (next >= 0 && next < container.ChildCount)
		{
			next = WalkFromCheckpoint(container, container.FindCheckpoint(next), firstIndex, visit);
		}
	}

	// Returns the child to resume at when an indexed child had to be jumped over (the child after one always has
	// its own checkpoint), or -1 when finished.
	private int WalkFromCheckpoint(IndexedContainer container, int checkpoint, int firstIndex, ChildVisitor visit)
	{
		int index = container.CheckpointIndexes[checkpoint];
		byte opening = container.Kind == JsonValueKind.Object ? (byte)'{' : (byte)'[';
		using var feeder = new JsonBlockFeeder(_bytes, container.CheckpointOffsets[checkpoint], container.End,
			WalkBlockBytes, opening);

		var state = new JsonReaderState(JsonFormatting.LenientReader);
		string? name = null;
		long childStart = 0;
		int childCount = 0;
		JsonValueKind childKind = default;

		while (true)
		{
			var reader = new Utf8JsonReader(feeder.Window.Span, feeder.IsFinal, state);
			while (reader.Read())
			{
				// A depth of 0 is the container, 1 its children, 2 their children.
				int depth = reader.CurrentDepth;
				switch (reader.TokenType)
				{
					case JsonTokenType.PropertyName:
						if (depth == 1 && index >= firstIndex)
						{
							name = reader.GetString();
						}

						break;

					case JsonTokenType.StartObject:
					case JsonTokenType.StartArray:
						if (depth == 2)
						{
							childCount++;
						}
						else if (depth == 1)
						{
							childStart = feeder.WindowOffset + reader.TokenStartIndex;
							childKind = reader.TokenType == JsonTokenType.StartObject
								? JsonValueKind.Object
								: JsonValueKind.Array;
							childCount = 0;

							if (_index.TryGetContainer(childStart, out IndexedContainer? indexed))
							{
								if (index >= firstIndex && !visit(index,
									    new IndexedChild(name, indexed.Start, indexed.End, indexed.Kind, indexed.ChildCount),
									    null))
								{
									return -1;
								}

								return index + 1;
							}
						}

						break;

					case JsonTokenType.EndObject:
					case JsonTokenType.EndArray:
						if (depth == 0)
						{
							return -1;
						}

						if (depth == 1)
						{
							long childEnd = feeder.WindowOffset + reader.BytesConsumed;
							if (index >= firstIndex && !visit(index,
								    new IndexedChild(name, childStart, childEnd, childKind, childCount), null))
							{
								return -1;
							}

							index++;
							name = null;
						}

						break;

					default:
						if (depth == 2)
						{
							childCount++;
						}
						else if (depth == 1)
						{
							if (index >= firstIndex)
							{
								long start = feeder.WindowOffset + reader.TokenStartIndex;
								long end = feeder.WindowOffset + reader.BytesConsumed;
								JsonValueKind kind = reader.TokenType switch
								{
									JsonTokenType.String => JsonValueKind.String,
									JsonTokenType.Number => JsonValueKind.Number,
									JsonTokenType.True => JsonValueKind.True,
									JsonTokenType.False => JsonValueKind.False,
									_ => JsonValueKind.Null
								};

								if (!visit(index, new IndexedChild(name, start, end, kind, 0), ReadPrimitive(ref reader)))
								{
									return -1;
								}
							}

							index++;
							name = null;
						}

						break;
				}
			}

			if (feeder.IsFinal)
			{
				return -1;
			}

			state = reader.CurrentState;
			feeder.Advance((int)reader.BytesConsumed);
		}
	}

	private IEnumerable<KeyValuePair<string, JsonElement>> EnumerateProperties(JsonElement obj)
	{
		if (_index.MayHaveDuplicateKeys)
		{
			return JsonObjectReader.GetProperties(obj);
		}

		return EnumerateRaw(obj);

		static IEnumerable<KeyValuePair<string, JsonElement>> EnumerateRaw(JsonElement element)
		{
			foreach (JsonProperty prop in element.EnumerateObject())
			{
				yield return new KeyValuePair<string, JsonElement>(prop.Name, prop.Value);
			}
		}
	}

	private static string? GetPrimitiveRawValue(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.GetRawText(),
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.Null => "null",
			_ => null
		};
	}

	#endregion

	private readonly record struct ChildListKey(string Path, int Start, int Count);

	private sealed class ParsedSubtree(JsonDocument document)
	{
		public JsonDocument Document { get; } = document;

		public ConcurrentDictionary<string, JsonElement> Containers { get; } = new(StringComparer.Ordinal);
	}
}
