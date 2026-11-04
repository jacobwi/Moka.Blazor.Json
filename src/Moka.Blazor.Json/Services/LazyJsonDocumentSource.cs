using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     An <see cref="IJsonDocumentSource" /> implementation that holds raw JSON bytes
///     and a structural index, parsing subtrees on demand with LRU caching.
///     Designed for large documents (50MB+) where full DOM parsing is not feasible.
/// </summary>
internal sealed class LazyJsonDocumentSource : IJsonDocumentSource
{
	private readonly byte[] _buffer;
	private readonly object _cacheLock = new();
	private readonly JsonStructuralIndex _index;
	private readonly ILogger _logger;
	private readonly LruCache<string, JsonDocument> _subtreeCache;
	private readonly bool _mayHaveDuplicateKeys;
	private bool _disposed;

	private LazyJsonDocumentSource(byte[] buffer, JsonStructuralIndex index, bool mayHaveDuplicateKeys,
		TimeSpan parseTime, ILogger logger)
	{
		_buffer = buffer;
		_index = index;
		_mayHaveDuplicateKeys = mayHaveDuplicateKeys;
		ParseTime = parseTime;
		_logger = logger;
		// Callers on other threads may still be reading an element from an evicted document
		// (search and flatten run concurrently), so eviction leaves disposal to the GC.
		_subtreeCache = new LruCache<string, JsonDocument>(50, disposeOnEvict: false);
		DocumentSizeBytes = buffer.Length;
		RootValueKind = DetermineRootValueKind();

		DebugStats = new LazyDebugStats
		{
			TotalBytes = buffer.Length,
			IndexEntries = index.EntryCount
		};
	}

	/// <summary>
	///     Debug statistics for the lazy parsing session. Always populated.
	///     Consumed by the Moka.Blazor.Json.Diagnostics overlay.
	/// </summary>
	internal LazyDebugStats DebugStats { get; }

	/// <summary>
	///     Creates a <see cref="LazyJsonDocumentSource" /> from a stream, reading it fully into memory
	///     and building a structural index.
	/// </summary>
	public static async Task<LazyJsonDocumentSource> CreateAsync(
		Stream stream,
		ILogger logger,
		MokaJsonViewerOptions options,
		CancellationToken cancellationToken = default)
	{
		using var ms = new MemoryStream();
		await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
		byte[] buffer = ms.ToArray();
		return await CreateFromBufferAsync(buffer, logger, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	///     Creates a <see cref="LazyJsonDocumentSource" /> from a UTF-8 byte array.
	/// </summary>
	public static async Task<LazyJsonDocumentSource> CreateAsync(
		byte[] buffer,
		ILogger logger,
		MokaJsonViewerOptions options,
		CancellationToken cancellationToken = default) =>
		await CreateFromBufferAsync(buffer, logger, cancellationToken).ConfigureAwait(false);

	/// <summary>
	///     Creates a <see cref="LazyJsonDocumentSource" /> from a JSON string.
	/// </summary>
	public static async Task<LazyJsonDocumentSource> CreateFromStringAsync(
		string json,
		ILogger logger,
		MokaJsonViewerOptions options,
		CancellationToken cancellationToken = default)
	{
		// Offload byte conversion to background thread — can be hundreds of MB
		byte[] buffer = await Task.Run(() => Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
		return await CreateFromBufferAsync(buffer, logger, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<LazyJsonDocumentSource> CreateFromBufferAsync(
		byte[] buffer,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		// Strip UTF-8 BOM (0xEF 0xBB 0xBF) if present
		if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
		{
			buffer = buffer[3..];
		}

		var sw = Stopwatch.StartNew();
		byte[] json = buffer;
		Task<bool> duplicateScan = Task.Run(() => JsonObjectReader.MayContainDuplicateKeys(json), cancellationToken);
		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(
			buffer, 1, cancellationToken).ConfigureAwait(false);
		bool mayHaveDuplicateKeys = await duplicateScan.ConfigureAwait(false);
		sw.Stop();

		logger.LogDebug("Built lazy JSON index: {Size} bytes, {Entries} entries, {Time}ms",
			buffer.Length, index.EntryCount, sw.Elapsed.TotalMilliseconds);

		return new LazyJsonDocumentSource(buffer, index, mayHaveDuplicateKeys, sw.Elapsed, logger);
	}

	#region IJsonDocumentSource

	public bool IsLoaded => true;
	public long DocumentSizeBytes { get; }
	public TimeSpan ParseTime { get; }
	public JsonValueKind RootValueKind { get; }
	public bool SupportsEditing => false;

	public int GetChildCount(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		string normalizedPath = NormalizePath(path);

		if (_index.TryGetEntry(normalizedPath, out JsonStructuralIndex.IndexEntry entry))
		{
			if (entry.ChildCount >= 0)
			{
				return entry.ChildCount;
			}

			// Child count unknown — need to index children lazily
			DebugStats.RecordLazyIndex();
			_index.IndexChildrenAsync(normalizedPath, _buffer, CancellationToken.None).GetAwaiter().GetResult();

			// Re-read the entry — it may have been updated, or count children from index
			int count = 0;
			foreach (JsonStructuralIndex.IndexEntry _ in _index.GetDirectChildren(normalizedPath))
			{
				count++;
			}

			return count;
		}

		// Not in index — parse the container to count
		return GetElementChildCount(GetElement(path));
	}

	public IEnumerable<JsonChildDescriptor> EnumerateChildren(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		string normalizedPath = NormalizePath(path);

		// Parse the container to guarantee correct ordering.
		// IMPORTANT: Materialize into a list eagerly — do NOT use yield return here.
		// The JsonElement enumerators hold references to the backing JsonDocument,
		// which can be evicted from the LRU cache and disposed if another GetElement
		// call happens between yields (e.g., during ExpandToDepth recursion).
		JsonElement element = GetElement(path);
		var result = new List<JsonChildDescriptor>();

		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (KeyValuePair<string, JsonElement> prop in EnumerateProperties(element))
			{
				string childPath = $"{normalizedPath}/{JsonPointerHelper.EscapeSegment(prop.Key)}";
				JsonElement value = prop.Value;

				int childCount;
				if (_index.TryGetEntry(childPath, out JsonStructuralIndex.IndexEntry entry) && entry.ChildCount >= 0)
				{
					childCount = entry.ChildCount;
				}
				else
				{
					childCount = GetElementChildCount(value);
				}

				result.Add(new JsonChildDescriptor(
					childPath,
					prop.Key,
					null,
					value.ValueKind,
					value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
						? null
						: GetPrimitiveRawValue(value),
					childCount));
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			int i = 0;
			foreach (JsonElement item in element.EnumerateArray())
			{
				string childPath = $"{normalizedPath}/{i}";

				int childCount;
				if (_index.TryGetEntry(childPath, out JsonStructuralIndex.IndexEntry entry) && entry.ChildCount >= 0)
				{
					childCount = entry.ChildCount;
				}
				else
				{
					childCount = GetElementChildCount(item);
				}

				result.Add(new JsonChildDescriptor(
					childPath,
					null,
					i,
					item.ValueKind,
					item.ValueKind is JsonValueKind.Object or JsonValueKind.Array
						? null
						: GetPrimitiveRawValue(item),
					childCount));
				i++;
			}
		}

		return result;
	}

	public string? GetRawValue(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		string normalizedPath = NormalizePath(path);

		if (_index.TryGetEntry(normalizedPath, out JsonStructuralIndex.IndexEntry entry) &&
		    entry.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
		{
			return GetRawValueFromBytes(entry);
		}

		JsonElement element = GetElement(path);
		return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
			? null
			: GetPrimitiveRawValue(element);
	}

	public JsonValueKind GetValueKind(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		string normalizedPath = NormalizePath(path);

		if (_index.TryGetEntry(normalizedPath, out JsonStructuralIndex.IndexEntry entry))
		{
			return entry.ValueKind;
		}

		// Not in index — parse parent
		JsonElement element = GetElement(path);
		return element.ValueKind;
	}

	public JsonElement GetElement(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		string normalizedPath = NormalizePath(path);

		// Find the best indexed ancestor to parse
		string parsePath = FindBestIndexedAncestor(normalizedPath);

		lock (_cacheLock)
		{
			if (!_subtreeCache.TryGet(parsePath, out JsonDocument? doc))
			{
				DebugStats.RecordCacheMiss();

				if (_index.TryGetEntry(parsePath, out JsonStructuralIndex.IndexEntry entry))
				{
					long sliceLen = entry.EndOffset - entry.StartOffset;
					ReadOnlyMemory<byte> slice = _buffer.AsMemory(
						(int)entry.StartOffset,
						(int)sliceLen);

					var sw = Stopwatch.StartNew();
					doc = JsonDocument.Parse(slice, new JsonDocumentOptions
					{
						AllowTrailingCommas = true,
						CommentHandling = JsonCommentHandling.Skip,
						MaxDepth = 256
					});
					sw.Stop();

					_subtreeCache.Set(parsePath, doc);
					DebugStats.RecordParse(parsePath, entry.StartOffset, sliceLen, sw.Elapsed);
				}
				else
				{
					// Parse entire document as fallback (for root)
					var sw = Stopwatch.StartNew();
					doc = JsonDocument.Parse(_buffer.AsMemory(), new JsonDocumentOptions
					{
						AllowTrailingCommas = true,
						CommentHandling = JsonCommentHandling.Skip,
						MaxDepth = 256
					});
					sw.Stop();

					_subtreeCache.Set("", doc);
					parsePath = "";
					DebugStats.RecordParse("(root)", 0, _buffer.Length, sw.Elapsed);
				}
			}
			else
			{
				DebugStats.RecordCacheHit();
			}

			// Navigate from the parsed subtree root to the requested path
			if (parsePath == normalizedPath ||
			    (string.IsNullOrEmpty(parsePath) && string.IsNullOrEmpty(normalizedPath)))
			{
				return doc.RootElement;
			}

			// Navigate relative path from parsePath to normalizedPath
			string relativePath = normalizedPath[parsePath.Length..];
			return NavigateElement(doc.RootElement, relativePath);
		}
	}

	public string GetJsonString(bool indented = true)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		// Re-write even when not indented: the raw buffer keeps the source's own formatting,
		// comments and trailing commas, so it is neither minified nor strict JSON.
		using var doc = JsonDocument.Parse(_buffer.AsMemory(), JsonFormatting.LenientDocument);

		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream, JsonFormatting.Writer(indented));
		doc.RootElement.WriteTo(writer);
		writer.Flush();
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	public int CountNodes() => -1;

	public int GetMaxDepth() => -1;

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		lock (_cacheLock)
		{
			_subtreeCache.Clear();
		}

		return ValueTask.CompletedTask;
	}

	#endregion

	#region Private Helpers

	private static string NormalizePath(string? path) => path ?? "";

	private JsonValueKind DetermineRootValueKind()
	{
		if (_index.TryGetEntry("", out JsonStructuralIndex.IndexEntry entry))
		{
			return entry.ValueKind;
		}

		// Peek at first byte
		ReadOnlySpan<byte> span = _buffer;
		for (int i = 0; i < span.Length; i++)
		{
			byte b = span[i];
			if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
			{
				continue;
			}

			return b switch
			{
				(byte)'{' => JsonValueKind.Object,
				(byte)'[' => JsonValueKind.Array,
				(byte)'"' => JsonValueKind.String,
				(byte)'t' => JsonValueKind.True,
				(byte)'f' => JsonValueKind.False,
				(byte)'n' => JsonValueKind.Null,
				_ => JsonValueKind.Number
			};
		}

		return JsonValueKind.Undefined;
	}

	private string? GetRawValueFromBytes(JsonStructuralIndex.IndexEntry entry)
	{
		ReadOnlySpan<byte> slice = _buffer.AsSpan(
			(int)entry.StartOffset,
			(int)(entry.EndOffset - entry.StartOffset));

		return entry.ValueKind switch
		{
			JsonValueKind.String => DecodeJsonString(slice),
			JsonValueKind.Number => Encoding.UTF8.GetString(slice),
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.Null => "null",
			_ => null
		};
	}

	private static string? DecodeJsonString(ReadOnlySpan<byte> slice)
	{
		// The slice includes the quotes — parse it as JSON to properly decode escapes
		try
		{
			var reader = new Utf8JsonReader(slice);
			if (reader.Read() && reader.TokenType == JsonTokenType.String)
			{
				return reader.GetString();
			}
		}
		catch (JsonException)
		{
		}

		// Fallback: strip quotes
		if (slice.Length >= 2 && slice[0] == (byte)'"' && slice[^1] == (byte)'"')
		{
			return Encoding.UTF8.GetString(slice[1..^1]);
		}

		return Encoding.UTF8.GetString(slice);
	}

	private string FindBestIndexedAncestor(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return "";
		}

		// Try the path itself first
		if (_index.TryGetEntry(path, out JsonStructuralIndex.IndexEntry entry) &&
		    entry.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
		{
			return path;
		}

		// Walk up to find the nearest indexed container ancestor
		string current = path;
		while (true)
		{
			int lastSlash = current.LastIndexOf('/');
			if (lastSlash <= 0)
			{
				return "";
			}

			current = current[..lastSlash];
			if (_index.TryGetEntry(current, out entry) &&
			    entry.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				return current;
			}
		}
	}

	private static JsonElement NavigateElement(JsonElement element, string relativePath)
	{
		if (string.IsNullOrEmpty(relativePath))
		{
			return element;
		}

		JsonElement current = element;
		string[] segments = JsonPointerHelper.Split(relativePath);

		foreach (string segment in segments)
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
				if (!int.TryParse(unescaped, out int index) || index < 0 || index >= current.GetArrayLength())
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

		return current;
	}

	private IEnumerable<KeyValuePair<string, JsonElement>> EnumerateProperties(JsonElement obj)
	{
		if (_mayHaveDuplicateKeys)
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

	private int GetElementChildCount(JsonElement element) =>
		JsonObjectReader.CountChildren(element, _mayHaveDuplicateKeys);

	private static string GetLastSegment(string path)
	{
		int lastSlash = path.LastIndexOf('/');
		return lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
	}

	#endregion
}
