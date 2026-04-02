using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Manages the parsed representation of a JSON document, choosing the
///     optimal parsing strategy based on document size and usage mode.
/// </summary>
internal sealed class JsonDocumentManager(ILogger<JsonDocumentManager> logger, IOptions<MokaJsonViewerOptions> options)
	: IJsonDocumentSource
{
	#region IAsyncDisposable

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		DisposeDocument();
		return ValueTask.CompletedTask;
	}

	#endregion

	#region Fields

	private readonly MokaJsonViewerOptions _options = options.Value;
	private JsonDocument? _document;
	private byte[]? _rentedBuffer;
	private TimeSpan _parseTime;
	private bool _disposed;

	#endregion

	#region Properties

	/// <summary>
	///     Gets the root <see cref="JsonElement" /> of the parsed document.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if no document is loaded.</exception>
	public JsonElement RootElement =>
		_document?.RootElement ?? throw new InvalidOperationException("No document is loaded.");

	/// <summary>
	///     Gets the size of the document in bytes (UTF-8 encoded).
	/// </summary>
	public long DocumentSizeBytes { get; private set; }

	/// <summary>
	///     Gets how long the initial parse took.
	/// </summary>
	public TimeSpan ParseTime => _parseTime;

	/// <summary>
	///     Gets whether a document is currently loaded.
	/// </summary>
	public bool IsLoaded => _document is not null;

	/// <inheritdoc />
	public JsonValueKind RootValueKind =>
		_document?.RootElement.ValueKind ?? throw new InvalidOperationException("No document is loaded.");

	/// <inheritdoc />
	public bool SupportsEditing => true;

	#endregion

	#region Public Methods

	/// <summary>
	///     Parses a JSON string and stores the resulting <see cref="JsonDocument" />.
	/// </summary>
	/// <param name="json">The JSON string to parse.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="ArgumentException">Thrown when the JSON string is null or whitespace.</exception>
	/// <exception cref="InvalidOperationException">Thrown when the document exceeds the configured size limit.</exception>
	public ValueTask ParseAsync(string json, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (string.IsNullOrWhiteSpace(json))
		{
			throw new ArgumentException("JSON string cannot be null or whitespace.", nameof(json));
		}

		// Strip UTF-8 BOM if present (U+FEFF)
		if (json.Length > 0 && json[0] == '\uFEFF')
		{
			json = json[1..];
		}

		int byteCount = Encoding.UTF8.GetByteCount(json);
		if (byteCount > _options.MaxDocumentSizeBytes)
		{
			throw new InvalidOperationException(
				$"Document size ({FormatBytes(byteCount)}) exceeds the maximum allowed size ({FormatBytes(_options.MaxDocumentSizeBytes)}).");
		}

		DisposeDocument();

		var sw = Stopwatch.StartNew();
		_document = JsonDocument.Parse(json, new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
			MaxDepth = 256
		});
		sw.Stop();

		DocumentSizeBytes = byteCount;
		_parseTime = sw.Elapsed;
		logger.LogDebug("Parsed JSON document: {Size}, {ParseTime}ms", FormatBytes(byteCount),
			_parseTime.TotalMilliseconds);

		return ValueTask.CompletedTask;
	}

	/// <summary>
	///     Parses a JSON stream and stores the resulting <see cref="JsonDocument" />.
	/// </summary>
	/// <param name="stream">The stream containing JSON data.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	public async ValueTask ParseAsync(Stream stream, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(stream);

		DisposeDocument();

		var sw = Stopwatch.StartNew();

		if (stream.CanSeek)
		{
			DocumentSizeBytes = stream.Length;
			if (DocumentSizeBytes > _options.MaxDocumentSizeBytes)
			{
				throw new InvalidOperationException(
					$"Document size ({FormatBytes(DocumentSizeBytes)}) exceeds the maximum allowed size ({FormatBytes(_options.MaxDocumentSizeBytes)}).");
			}
		}

		// Skip UTF-8 BOM (0xEF 0xBB 0xBF) if present
		await SkipBomAsync(stream, cancellationToken).ConfigureAwait(false);

		// For non-seekable streams, wrap in a counting stream to track bytes read
		using CountingStream? countingStream = !stream.CanSeek ? new CountingStream(stream) : null;

		_document = await JsonDocument.ParseAsync(countingStream ?? stream, new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
			MaxDepth = 256
		}, cancellationToken).ConfigureAwait(false);

		sw.Stop();

		if (countingStream is not null)
		{
			DocumentSizeBytes = countingStream.BytesRead;
		}

		_parseTime = sw.Elapsed;
		logger.LogDebug("Parsed JSON stream: {Size}, {ParseTime}ms", FormatBytes(DocumentSizeBytes),
			_parseTime.TotalMilliseconds);
	}

	/// <summary>
	///     Converts a subtree rooted at the given path to a mutable <see cref="JsonNode" /> for editing.
	/// </summary>
	/// <param name="jsonPointer">The JSON Pointer path to the subtree root.</param>
	/// <returns>The mutable <see cref="JsonNode" /> representation of the subtree.</returns>
	public JsonNode? GetMutableSubtree(string jsonPointer)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_document is null)
		{
			throw new InvalidOperationException("No document is loaded.");
		}

		JsonElement element = NavigateToElement(jsonPointer);
		return JsonNode.Parse(element.GetRawText());
	}

	/// <summary>
	///     Navigates to a <see cref="JsonElement" /> at the specified JSON Pointer path.
	/// </summary>
	/// <param name="jsonPointer">The JSON Pointer (RFC 6901) path.</param>
	/// <returns>The element at the specified path.</returns>
	/// <exception cref="KeyNotFoundException">Thrown when the path does not exist.</exception>
	public JsonElement NavigateToElement(string jsonPointer)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_document is null)
		{
			throw new InvalidOperationException("No document is loaded.");
		}

		if (string.IsNullOrEmpty(jsonPointer) || jsonPointer == "/")
		{
			return _document.RootElement;
		}

		JsonElement current = _document.RootElement;
		string[] segments = jsonPointer.Split('/', StringSplitOptions.RemoveEmptyEntries);

		foreach (string segment in segments)
		{
			string unescaped = JsonPointerHelper.UnescapeSegment(segment);

			if (current.ValueKind == JsonValueKind.Object)
			{
				if (!current.TryGetProperty(unescaped, out JsonElement next))
				{
					throw new KeyNotFoundException($"Property '{unescaped}' not found at path '{jsonPointer}'.");
				}

				current = next;
			}
			else if (current.ValueKind == JsonValueKind.Array)
			{
				if (!int.TryParse(unescaped, out int index))
				{
					throw new KeyNotFoundException($"Invalid array index '{unescaped}' at path '{jsonPointer}'.");
				}

				if (index < 0 || index >= current.GetArrayLength())
				{
					throw new KeyNotFoundException($"Array index {index} out of range at path '{jsonPointer}'.");
				}

				current = current[index];
			}
			else
			{
				throw new KeyNotFoundException(
					$"Cannot navigate into a {current.ValueKind} value at path '{jsonPointer}'.");
			}
		}

		return current;
	}

	/// <summary>
	///     Replaces the value at the given JSON Pointer path and returns the full modified JSON string.
	/// </summary>
	public string ReplaceValueAtPath(string jsonPointer, string jsonLiteral)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_document is null)
		{
			throw new InvalidOperationException("No document is loaded.");
		}

		if (string.IsNullOrEmpty(jsonPointer) || jsonPointer == "/")
		{
			// Replacing root
			return jsonLiteral;
		}

		var root = JsonNode.Parse(_document.RootElement.GetRawText());
		JsonNode? target = NavigateJsonNode(root, jsonPointer);
		JsonNode? parent = target?.Parent;
		var newValue = JsonNode.Parse(jsonLiteral);

		if (parent is JsonObject parentObj)
		{
			string key = GetLastSegment(jsonPointer);
			parentObj[key] = newValue;
		}
		else if (parent is JsonArray parentArr)
		{
			int index = int.Parse(GetLastSegment(jsonPointer), CultureInfo.InvariantCulture);
			parentArr[index] = newValue;
		}

		return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? jsonLiteral;
	}

	/// <summary>
	///     Renames a property key at the given path and returns the full modified JSON string.
	/// </summary>
	public string RenameKeyAtPath(string jsonPointer, string newKeyName)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_document is null)
		{
			throw new InvalidOperationException("No document is loaded.");
		}

		string parentPath = jsonPointer[..jsonPointer.LastIndexOf('/')];
		if (string.IsNullOrEmpty(parentPath))
		{
			parentPath = "";
		}

		var root = JsonNode.Parse(_document.RootElement.GetRawText());
		JsonNode? parentNode = string.IsNullOrEmpty(parentPath)
			? root
			: NavigateJsonNode(root, parentPath);

		if (parentNode is not JsonObject parentObj)
		{
			throw new InvalidOperationException("Parent is not an object.");
		}

		string oldKey = JsonPointerHelper.UnescapeSegment(GetLastSegment(jsonPointer));

		// Preserve insertion order by rebuilding
		var entries = new List<KeyValuePair<string, JsonNode?>>();
		foreach (KeyValuePair<string, JsonNode?> kvp in parentObj)
		{
			if (kvp.Key == oldKey)
			{
				entries.Add(new KeyValuePair<string, JsonNode?>(newKeyName, kvp.Value?.DeepClone()));
			}
			else
			{
				entries.Add(new KeyValuePair<string, JsonNode?>(kvp.Key, kvp.Value?.DeepClone()));
			}
		}

		parentObj.Clear();
		foreach (KeyValuePair<string, JsonNode?> entry in entries)
		{
			parentObj[entry.Key] = entry.Value;
		}

		return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "";
	}

	/// <summary>
	///     Removes the node at the given JSON Pointer path and returns the full modified JSON string.
	/// </summary>
	public string RemoveNodeAtPath(string jsonPointer)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_document is null)
		{
			throw new InvalidOperationException("No document is loaded.");
		}

		if (string.IsNullOrEmpty(jsonPointer) || jsonPointer == "/")
		{
			throw new InvalidOperationException("Cannot remove root node.");
		}

		string parentPath = jsonPointer[..jsonPointer.LastIndexOf('/')];
		if (string.IsNullOrEmpty(parentPath))
		{
			parentPath = "";
		}

		var root = JsonNode.Parse(_document.RootElement.GetRawText());
		JsonNode? parentNode = string.IsNullOrEmpty(parentPath)
			? root
			: NavigateJsonNode(root, parentPath);

		string lastSegment = JsonPointerHelper.UnescapeSegment(GetLastSegment(jsonPointer));

		if (parentNode is JsonObject parentObj)
		{
			parentObj.Remove(lastSegment);
		}
		else if (parentNode is JsonArray parentArr)
		{
			int index = int.Parse(lastSegment, CultureInfo.InvariantCulture);
			parentArr.RemoveAt(index);
		}

		return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "";
	}

	/// <summary>
	///     Adds a child node to the container at the given path and returns the full modified JSON string.
	/// </summary>
	public string AddNodeAtPath(string parentPath, string? propertyName, string valueJson)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_document is null)
		{
			throw new InvalidOperationException("No document is loaded.");
		}

		var root = JsonNode.Parse(_document.RootElement.GetRawText());
		JsonNode? parentNode = string.IsNullOrEmpty(parentPath)
			? root
			: NavigateJsonNode(root, parentPath);
		var newValue = JsonNode.Parse(valueJson);

		if (parentNode is JsonObject parentObj)
		{
			string key = propertyName ?? GenerateUniqueKey(parentObj);
			parentObj[key] = newValue;
		}
		else if (parentNode is JsonArray parentArr)
		{
			parentArr.Add(newValue);
		}

		return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "";
	}

	/// <summary>
	///     Counts all nodes in the document tree recursively.
	/// </summary>
	/// <returns>The total number of nodes.</returns>
	public int CountNodes()
	{
		if (_document is null)
		{
			return 0;
		}

		return CountNodes(_document.RootElement);
	}

	/// <summary>
	///     Gets the maximum depth of the document tree.
	/// </summary>
	/// <returns>The maximum depth (root = 0).</returns>
	public int GetMaxDepth()
	{
		if (_document is null)
		{
			return 0;
		}

		return GetMaxDepth(_document.RootElement, 0);
	}

	/// <summary>
	///     Gets the full JSON as a formatted string.
	/// </summary>
	/// <param name="indented">Whether to pretty-print the output.</param>
	/// <returns>The JSON string.</returns>
	public string GetJsonString(bool indented = true)
	{
		if (_document is null)
		{
			return string.Empty;
		}

		var options = new JsonWriterOptions { Indented = indented };
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream, options);
		_document.RootElement.WriteTo(writer);
		writer.Flush();
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	#endregion

	#region IJsonDocumentSource

	/// <inheritdoc />
	public int GetChildCount(string path)
	{
		JsonElement element = string.IsNullOrEmpty(path) || path == "/"
			? RootElement
			: NavigateToElement(path);

		return element.ValueKind switch
		{
			JsonValueKind.Object => CountObjectProperties(element),
			JsonValueKind.Array => element.GetArrayLength(),
			_ => 0
		};
	}

	/// <inheritdoc />
	public IEnumerable<JsonChildDescriptor> EnumerateChildren(string path)
	{
		JsonElement element = string.IsNullOrEmpty(path) || path == "/"
			? RootElement
			: NavigateToElement(path);

		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty prop in element.EnumerateObject())
			{
				string childPath = string.IsNullOrEmpty(path) || path == "/"
					? $"/{JsonPointerHelper.EscapeSegment(prop.Name)}"
					: $"{path}/{JsonPointerHelper.EscapeSegment(prop.Name)}";

				int childCount = prop.Value.ValueKind switch
				{
					JsonValueKind.Object => CountObjectProperties(prop.Value),
					JsonValueKind.Array => prop.Value.GetArrayLength(),
					_ => 0
				};

				yield return new JsonChildDescriptor(
					childPath,
					prop.Name,
					null,
					prop.Value.ValueKind,
					prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
						? null
						: GetPrimitiveRawValue(prop.Value),
					childCount);
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			int i = 0;
			foreach (JsonElement item in element.EnumerateArray())
			{
				string childPath = string.IsNullOrEmpty(path) || path == "/"
					? $"/{i}"
					: $"{path}/{i}";

				int childCount = item.ValueKind switch
				{
					JsonValueKind.Object => CountObjectProperties(item),
					JsonValueKind.Array => item.GetArrayLength(),
					_ => 0
				};

				yield return new JsonChildDescriptor(
					childPath,
					null,
					i,
					item.ValueKind,
					item.ValueKind is JsonValueKind.Object or JsonValueKind.Array
						? null
						: GetPrimitiveRawValue(item),
					childCount);
				i++;
			}
		}
	}

	/// <inheritdoc />
	public string? GetRawValue(string path)
	{
		JsonElement element = string.IsNullOrEmpty(path) || path == "/"
			? RootElement
			: NavigateToElement(path);

		return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
			? null
			: GetPrimitiveRawValue(element);
	}

	/// <inheritdoc />
	public JsonValueKind GetValueKind(string path)
	{
		JsonElement element = string.IsNullOrEmpty(path) || path == "/"
			? RootElement
			: NavigateToElement(path);

		return element.ValueKind;
	}

	/// <inheritdoc />
	public JsonElement GetElement(string path) => NavigateToElement(path);

	#endregion

	#region Private Methods

	/// <summary>
	///     Skips a UTF-8 BOM (0xEF 0xBB 0xBF) at the current stream position if present.
	///     For seekable streams, peeks and rewinds if no BOM found.
	///     For non-seekable streams, wraps in a buffered approach.
	/// </summary>
	private static async ValueTask SkipBomAsync(Stream stream, CancellationToken cancellationToken)
	{
		if (!stream.CanRead)
		{
			return;
		}

		byte[] buffer = new byte[3];

		if (stream.CanSeek)
		{
			long originalPosition = stream.Position;
			int bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

			if (bytesRead >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
			{
				// BOM found — stream is now positioned right after it
				return;
			}

			// No BOM — rewind to original position
			stream.Position = originalPosition;
		}
	}

	private static int CountObjectProperties(JsonElement element)
	{
		int count = 0;
		foreach (JsonProperty _ in element.EnumerateObject())
		{
			count++;
		}

		return count;
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

	private static int CountNodes(JsonElement element)
	{
		int count = 1;
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (JsonProperty prop in element.EnumerateObject())
				{
					count += CountNodes(prop.Value);
				}

				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray())
				{
					count += CountNodes(item);
				}

				break;
		}

		return count;
	}

	private static int GetMaxDepth(JsonElement element, int currentDepth)
	{
		int maxDepth = currentDepth;
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (JsonProperty prop in element.EnumerateObject())
				{
					maxDepth = Math.Max(maxDepth, GetMaxDepth(prop.Value, currentDepth + 1));
				}

				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray())
				{
					maxDepth = Math.Max(maxDepth, GetMaxDepth(item, currentDepth + 1));
				}

				break;
		}

		return maxDepth;
	}

	#endregion

	#region Nested Types

	/// <summary>
	///     A minimal pass-through stream that counts bytes read without buffering.
	/// </summary>
	private sealed class CountingStream(Stream inner) : Stream
	{
		public long BytesRead { get; private set; }

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
			int read = inner.Read(buffer, offset, count);
			BytesRead += read;
			return read;
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
			CancellationToken cancellationToken)
		{
			int read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
			BytesRead += read;
			return read;
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
			CancellationToken cancellationToken = default)
		{
			int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
			BytesRead += read;
			return read;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private static JsonNode? NavigateJsonNode(JsonNode? root, string jsonPointer)
	{
		if (root is null)
		{
			throw new InvalidOperationException("Root node is null.");
		}

		if (string.IsNullOrEmpty(jsonPointer) || jsonPointer == "/")
		{
			return root;
		}

		JsonNode? current = root;
		string[] segments = jsonPointer.Split('/', StringSplitOptions.RemoveEmptyEntries);

		foreach (string segment in segments)
		{
			string unescaped = JsonPointerHelper.UnescapeSegment(segment);
			if (current is JsonObject obj)
			{
				current = obj[unescaped];
			}
			else if (current is JsonArray arr && int.TryParse(unescaped, out int idx))
			{
				current = arr[idx];
			}
			else
			{
				throw new KeyNotFoundException($"Cannot navigate to '{jsonPointer}'.");
			}
		}

		return current;
	}

	private static string GetLastSegment(string jsonPointer)
	{
		int lastSlash = jsonPointer.LastIndexOf('/');
		return lastSlash >= 0 ? jsonPointer[(lastSlash + 1)..] : jsonPointer;
	}

	private static string GenerateUniqueKey(JsonObject obj)
	{
		string baseName = "newProperty";
		if (!obj.ContainsKey(baseName))
		{
			return baseName;
		}

		for (int i = 1;; i++)
		{
			string candidate = $"{baseName}{i}";
			if (!obj.ContainsKey(candidate))
			{
				return candidate;
			}
		}
	}

	internal static string FormatBytes(long bytes)
	{
		return bytes switch
		{
			< 1024 => $"{bytes} B",
			< 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
			< 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
			_ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
		};
	}

	private void DisposeDocument()
	{
		_document?.Dispose();
		_document = null;

		if (_rentedBuffer is not null)
		{
			ArrayPool<byte>.Shared.Return(_rentedBuffer);
			_rentedBuffer = null;
		}
	}

	#endregion
}
