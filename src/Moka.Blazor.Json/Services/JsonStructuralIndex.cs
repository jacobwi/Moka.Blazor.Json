using System.Collections.Concurrent;
using System.Text.Json;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     A lightweight structural index of a JSON document built from a single
///     <see cref="Utf8JsonReader" /> pass. Stores byte offsets for containers,
///     enabling on-demand subtree parsing without loading the full DOM.
/// </summary>
internal sealed class JsonStructuralIndex
{
	private readonly ConcurrentDictionary<string, IndexEntry> _entries = new();

	/// <summary>
	///     Gets the number of indexed entries.
	/// </summary>
	public int EntryCount => _entries.Count;

	/// <summary>
	///     Tries to get an entry by path.
	/// </summary>
	public bool TryGetEntry(string path, out IndexEntry entry) => _entries.TryGetValue(path, out entry);

	/// <summary>
	///     Gets all direct children entries of a given parent path.
	/// </summary>
	public IEnumerable<IndexEntry> GetDirectChildren(string parentPath)
	{
		string prefix = string.IsNullOrEmpty(parentPath) ? "/" : parentPath + "/";

		foreach (KeyValuePair<string, IndexEntry> kvp in _entries)
		{
			string path = kvp.Key;
			if (!path.StartsWith(prefix, StringComparison.Ordinal))
			{
				continue;
			}

			// Check it's a direct child (no further '/' after the prefix)
			ReadOnlySpan<char> remainder = path.AsSpan(prefix.Length);
			if (!remainder.Contains('/'))
			{
				yield return kvp.Value;
			}
		}
	}

	/// <summary>
	///     Builds the structural index from UTF-8 JSON bytes.
	///     Only indexes containers up to <paramref name="eagerDepthLimit" /> levels deep.
	/// </summary>
	public static Task<JsonStructuralIndex> BuildAsync(
		ReadOnlyMemory<byte> jsonBytes,
		int eagerDepthLimit = 1,
		CancellationToken cancellationToken = default) =>
		Task.Run(() => Build(jsonBytes.Span, eagerDepthLimit, cancellationToken), cancellationToken);

	/// <summary>
	///     Lazily indexes the immediate children of a container whose children
	///     are not yet in the index.
	/// </summary>
	public Task IndexChildrenAsync(
		string parentPath,
		ReadOnlyMemory<byte> jsonBytes,
		CancellationToken cancellationToken = default)
	{
		return Task.Run(() =>
		{
			if (!_entries.TryGetValue(parentPath, out IndexEntry parentEntry))
			{
				return;
			}

			if (parentEntry.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
			{
				return;
			}

			ReadOnlySpan<byte> slice = jsonBytes.Span.Slice(
				(int)parentEntry.StartOffset,
				(int)(parentEntry.EndOffset - parentEntry.StartOffset));

			var reader = new Utf8JsonReader(slice, new JsonReaderOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip,
				MaxDepth = 256
			});

			IndexContainerChildren(ref reader, parentPath, parentEntry.Depth, jsonBytes.Span,
				parentEntry.StartOffset, cancellationToken);
		}, cancellationToken);
	}

	private static JsonStructuralIndex Build(
		ReadOnlySpan<byte> jsonBytes,
		int eagerDepthLimit,
		CancellationToken cancellationToken)
	{
		var index = new JsonStructuralIndex();
		var reader = new Utf8JsonReader(jsonBytes, new JsonReaderOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
			MaxDepth = 256
		});

		// Stack to track open containers: (path, startOffset, depth, childCount)
		var containerStack =
			new Stack<(string Path, long StartOffset, int Depth, int ChildCount, JsonValueKind Kind)>();
		string? currentPropertyName = null;
		int tokensProcessed = 0;

		while (reader.Read())
		{
			if (++tokensProcessed % 10000 == 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
			}

			int currentDepth = containerStack.Count;

			switch (reader.TokenType)
			{
				case JsonTokenType.StartObject:
				case JsonTokenType.StartArray:
				{
					JsonValueKind kind = reader.TokenType == JsonTokenType.StartObject
						? JsonValueKind.Object
						: JsonValueKind.Array;

					string path = BuildPath(containerStack, currentPropertyName);
					long startOffset = reader.TokenStartIndex;

					// Increment parent's child count
					if (containerStack.Count > 0)
					{
						(string pPath, long pStart, int pDepth, int pCount, JsonValueKind pKind) = containerStack.Pop();
						containerStack.Push((pPath, pStart, pDepth, pCount + 1, pKind));
					}

					if (currentDepth > eagerDepthLimit)
					{
						// Beyond eager limit: skip the entire subtree
						reader.TrySkip();
						long endOffset = reader.BytesConsumed;

						index._entries.TryAdd(path, new IndexEntry(
							path, startOffset, endOffset, kind, -1, currentDepth));
					}
					else
					{
						containerStack.Push((path, startOffset, currentDepth, 0, kind));
					}

					currentPropertyName = null;
					break;
				}

				case JsonTokenType.EndObject:
				case JsonTokenType.EndArray:
				{
					if (containerStack.Count > 0)
					{
						(string cPath, long cStart, int cDepth, int cChildCount, JsonValueKind cKind) =
							containerStack.Pop();
						long endOffset = reader.BytesConsumed;

						index._entries.TryAdd(cPath, new IndexEntry(
							cPath, cStart, endOffset, cKind, cChildCount, cDepth));
					}

					break;
				}

				case JsonTokenType.PropertyName:
				{
					currentPropertyName = reader.GetString();
					break;
				}

				default:
				{
					// Primitive value — increment parent child count
					if (containerStack.Count > 0)
					{
						(string pPath, long pStart, int pDepth, int pCount, JsonValueKind pKind) = containerStack.Pop();
						containerStack.Push((pPath, pStart, pDepth, pCount + 1, pKind));
					}

					// Index primitive children of indexed containers
					if (currentDepth <= eagerDepthLimit + 1)
					{
						string path = BuildPath(containerStack, currentPropertyName);
						JsonValueKind valueKind = reader.TokenType switch
						{
							JsonTokenType.String => JsonValueKind.String,
							JsonTokenType.Number => JsonValueKind.Number,
							JsonTokenType.True => JsonValueKind.True,
							JsonTokenType.False => JsonValueKind.False,
							JsonTokenType.Null => JsonValueKind.Null,
							_ => JsonValueKind.Undefined
						};

						index._entries.TryAdd(path, new IndexEntry(
							path, reader.TokenStartIndex, reader.BytesConsumed, valueKind, 0, currentDepth));
					}

					currentPropertyName = null;
					break;
				}
			}
		}

		return index;
	}

	private void IndexContainerChildren(
		ref Utf8JsonReader reader,
		string parentPath,
		int parentDepth,
		ReadOnlySpan<byte> fullBytes,
		long baseOffset,
		CancellationToken cancellationToken)
	{
		// Read the opening token
		if (!reader.Read())
		{
			return;
		}

		string? currentPropertyName = null;
		int childIndex = 0;
		int tokensProcessed = 0;

		while (reader.Read())
		{
			if (++tokensProcessed % 5000 == 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
			}

			switch (reader.TokenType)
			{
				case JsonTokenType.EndObject:
				case JsonTokenType.EndArray:
					return;

				case JsonTokenType.PropertyName:
					currentPropertyName = reader.GetString();
					break;

				case JsonTokenType.StartObject:
				case JsonTokenType.StartArray:
				{
					JsonValueKind kind = reader.TokenType == JsonTokenType.StartObject
						? JsonValueKind.Object
						: JsonValueKind.Array;

					string childPath = currentPropertyName is not null
						? $"{parentPath}/{EscapeJsonPointer(currentPropertyName)}"
						: $"{parentPath}/{childIndex}";

					long startOffset = baseOffset + reader.TokenStartIndex;

					// Skip the container to find its end
					reader.TrySkip();
					long endOffset = baseOffset + reader.BytesConsumed;

					_entries.TryAdd(childPath, new IndexEntry(
						childPath, startOffset, endOffset, kind, -1, parentDepth + 1));

					currentPropertyName = null;
					childIndex++;
					break;
				}

				default:
				{
					string childPath = currentPropertyName is not null
						? $"{parentPath}/{EscapeJsonPointer(currentPropertyName)}"
						: $"{parentPath}/{childIndex}";

					JsonValueKind valueKind = reader.TokenType switch
					{
						JsonTokenType.String => JsonValueKind.String,
						JsonTokenType.Number => JsonValueKind.Number,
						JsonTokenType.True => JsonValueKind.True,
						JsonTokenType.False => JsonValueKind.False,
						JsonTokenType.Null => JsonValueKind.Null,
						_ => JsonValueKind.Undefined
					};

					_entries.TryAdd(childPath, new IndexEntry(
						childPath,
						baseOffset + reader.TokenStartIndex,
						baseOffset + reader.BytesConsumed,
						valueKind,
						0,
						parentDepth + 1));

					currentPropertyName = null;
					childIndex++;
					break;
				}
			}
		}
	}

	private static string BuildPath(
		Stack<(string Path, long StartOffset, int Depth, int ChildCount, JsonValueKind Kind)> containerStack,
		string? propertyName)
	{
		if (containerStack.Count == 0)
		{
			return "";
		}

		(string parentPath, _, _, int childCount, JsonValueKind parentKind) = containerStack.Peek();

		if (parentKind == JsonValueKind.Object && propertyName is not null)
		{
			return string.IsNullOrEmpty(parentPath)
				? $"/{EscapeJsonPointer(propertyName)}"
				: $"{parentPath}/{EscapeJsonPointer(propertyName)}";
		}

		// Array: use current child count as index (before increment)
		return string.IsNullOrEmpty(parentPath)
			? $"/{childCount}"
			: $"{parentPath}/{childCount}";
	}

	private static string EscapeJsonPointer(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

	/// <summary>
	///     An entry in the structural index describing a single JSON node.
	/// </summary>
	internal readonly record struct IndexEntry(
		string Path,
		long StartOffset,
		long EndOffset,
		JsonValueKind ValueKind,
		int ChildCount,
		int Depth);
}
