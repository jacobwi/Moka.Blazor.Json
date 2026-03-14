using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Manages the parsed representation of a JSON document, choosing the
///     optimal parsing strategy based on document size and usage mode.
/// </summary>
internal sealed class JsonDocumentManager : IAsyncDisposable
{
    #region Constructor

    public JsonDocumentManager(ILogger<JsonDocumentManager> logger, IOptions<MokaJsonViewerOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        DisposeDocument();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Fields

    private readonly ILogger<JsonDocumentManager> _logger;
    private readonly MokaJsonViewerOptions _options;
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
            throw new ArgumentException("JSON string cannot be null or whitespace.", nameof(json));

        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > _options.MaxDocumentSizeBytes)
            throw new InvalidOperationException(
                $"Document size ({FormatBytes(byteCount)}) exceeds the maximum allowed size ({FormatBytes(_options.MaxDocumentSizeBytes)}).");

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
        _logger.LogDebug("Parsed JSON document: {Size}, {ParseTime}ms", FormatBytes(byteCount),
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
                throw new InvalidOperationException(
                    $"Document size ({FormatBytes(DocumentSizeBytes)}) exceeds the maximum allowed size ({FormatBytes(_options.MaxDocumentSizeBytes)}).");
        }

        // For non-seekable streams, wrap in a counting stream to track bytes read
        using var countingStream = !stream.CanSeek ? new CountingStream(stream) : null;

        _document = await JsonDocument.ParseAsync(countingStream ?? stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 256
        }, cancellationToken).ConfigureAwait(false);

        sw.Stop();

        if (countingStream is not null) DocumentSizeBytes = countingStream.BytesRead;

        _parseTime = sw.Elapsed;
        _logger.LogDebug("Parsed JSON stream: {Size}, {ParseTime}ms", FormatBytes(DocumentSizeBytes),
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

        if (_document is null) throw new InvalidOperationException("No document is loaded.");

        var element = NavigateToElement(jsonPointer);
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

        if (_document is null) throw new InvalidOperationException("No document is loaded.");

        if (string.IsNullOrEmpty(jsonPointer) || jsonPointer == "/") return _document.RootElement;

        var current = _document.RootElement;
        var segments = jsonPointer.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var unescaped = segment.Replace("~1", "/").Replace("~0", "~");

            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(unescaped, out var next))
                    throw new KeyNotFoundException($"Property '{unescaped}' not found at path '{jsonPointer}'.");
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                if (!int.TryParse(unescaped, out var index))
                    throw new KeyNotFoundException($"Invalid array index '{unescaped}' at path '{jsonPointer}'.");
                if (index < 0 || index >= current.GetArrayLength())
                    throw new KeyNotFoundException($"Array index {index} out of range at path '{jsonPointer}'.");
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
    ///     Counts all nodes in the document tree recursively.
    /// </summary>
    /// <returns>The total number of nodes.</returns>
    public int CountNodes()
    {
        if (_document is null) return 0;
        return CountNodes(_document.RootElement);
    }

    /// <summary>
    ///     Gets the maximum depth of the document tree.
    /// </summary>
    /// <returns>The maximum depth (root = 0).</returns>
    public int GetMaxDepth()
    {
        if (_document is null) return 0;
        return GetMaxDepth(_document.RootElement, 0);
    }

    /// <summary>
    ///     Gets the full JSON as a formatted string.
    /// </summary>
    /// <param name="indented">Whether to pretty-print the output.</param>
    /// <returns>The JSON string.</returns>
    public string GetJsonString(bool indented = true)
    {
        if (_document is null) return string.Empty;

        var options = new JsonWriterOptions { Indented = indented };
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, options);
        _document.RootElement.WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    #endregion

    #region Private Methods

    private static int CountNodes(JsonElement element)
    {
        var count = 1;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject()) count += CountNodes(prop.Value);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) count += CountNodes(item);
                break;
        }

        return count;
    }

    private static int GetMaxDepth(JsonElement element, int currentDepth)
    {
        var maxDepth = currentDepth;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    maxDepth = Math.Max(maxDepth, GetMaxDepth(prop.Value, currentDepth + 1));
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    maxDepth = Math.Max(maxDepth, GetMaxDepth(item, currentDepth + 1));
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
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesRead += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
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