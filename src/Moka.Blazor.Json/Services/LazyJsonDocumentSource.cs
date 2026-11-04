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
		bool mayHaveDuplicateKeys = await Task.Run(
			() => JsonObjectReader.MayContainDuplicateKeys(json), cancellationToken).ConfigureAwait(false);
		JsonStructuralIndex index = await JsonStructuralIndex.BuildAsync(
			buffer, 1, mayHaveDuplicateKeys, cancellationToken).ConfigureAwait(false);
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

		// The index counts every key, so with a repeated key it would count one twice.
		if (!_mayHaveDuplicateKeys && _index.TryGetEntry(normalizedPath, out JsonStructuralIndex.IndexEntry entry))
		{
			if (entry.ChildCount >= 0)
			{
				return entry.ChildCount;
			}

			DebugStats.RecordLazyIndex();
			return _index.IndexChildren(normalizedPath, _buffer, mayHaveDuplicateKeys: false);
		}

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
				if (!_mayHaveDuplicateKeys && _index.TryGetEntry(childPath, out JsonStructuralIndex.IndexEntry entry) &&
				    entry.ChildCount >= 0)
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
				if (!_mayHaveDuplicateKeys && _index.TryGetEntry(childPath, out JsonStructuralIndex.IndexEntry entry) &&
				    entry.ChildCount >= 0)
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
		string parsePath = FindBestIndexedAncestor(normalizedPath);
		JsonDocument doc = GetOrParseSubtree(parsePath);

		return parsePath == normalizedPath
			? doc.RootElement
			: NavigateElement(doc.RootElement, normalizedPath[parsePath.Length..]);
	}

	// Parsing happens outside the cache lock so a cached lookup (often on the UI thread) never waits for
	// another thread's parse. Two threads may parse the same subtree at once; the first one cached wins.
	private JsonDocument GetOrParseSubtree(string parsePath)
	{
		lock (_cacheLock)
		{
			if (_subtreeCache.TryGet(parsePath, out JsonDocument? cached))
			{
				DebugStats.RecordCacheHit();
				return cached;
			}
		}

		DebugStats.RecordCacheMiss();

		// Only the root can be missing from the index (a document whose root is a primitive).
		long start = 0;
		long length = _buffer.Length;
		if (_index.TryGetEntry(parsePath, out JsonStructuralIndex.IndexEntry entry))
		{
			start = entry.StartOffset;
			length = entry.EndOffset - entry.StartOffset;
		}

		var sw = Stopwatch.StartNew();
		JsonDocument parsed = JsonDocument.Parse(_buffer.AsMemory((int)start, (int)length),
			JsonFormatting.LenientDocument);
		sw.Stop();

		lock (_cacheLock)
		{
			if (_disposed)
			{
				parsed.Dispose();
				throw new ObjectDisposedException(nameof(LazyJsonDocumentSource));
			}

			if (_subtreeCache.TryGet(parsePath, out JsonDocument? raced))
			{
				parsed.Dispose();
				return raced;
			}

			_subtreeCache.Set(parsePath, parsed);
		}

		DebugStats.RecordParse(parsePath.Length == 0 ? "(root)" : parsePath, start, length, sw.Elapsed);
		return parsed;
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
