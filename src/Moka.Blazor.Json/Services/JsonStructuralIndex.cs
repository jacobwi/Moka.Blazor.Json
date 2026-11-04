using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     A lightweight structural index of a JSON document built from a single
///     <see cref="Utf8JsonReader" /> pass. Stores byte offsets for containers,
///     enabling on-demand subtree parsing without loading the full DOM.
/// </summary>
/// <remarks>
///     Only objects and arrays are indexed. Primitive values are read from their parsed parent, which keeps
///     the index small (indexing every value made it roughly ten times the size of the document).
/// </remarks>
internal sealed class JsonStructuralIndex
{
	private static readonly JsonReaderOptions ReaderOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip,
		MaxDepth = 256
	};

	private readonly ConcurrentDictionary<string, IndexEntry> _entries = new(StringComparer.Ordinal);

	/// <summary>
	///     Gets the number of indexed entries.
	/// </summary>
	public int EntryCount => _entries.Count;

	/// <summary>
	///     Tries to get an entry by path.
	/// </summary>
	public bool TryGetEntry(string path, out IndexEntry entry) => _entries.TryGetValue(path, out entry);

	/// <summary>
	///     Builds the structural index from UTF-8 JSON bytes.
	///     Only indexes containers up to <paramref name="eagerDepthLimit" /> levels deep.
	/// </summary>
	/// <param name="jsonBytes">The document.</param>
	/// <param name="eagerDepthLimit">Containers deeper than this are recorded but not read into.</param>
	/// <param name="mayHaveDuplicateKeys">
	///     Whether an object may repeat a key. When false, the work to make a later value replace an earlier
	///     one is skipped.
	/// </param>
	/// <param name="cancellationToken">Cancellation token.</param>
	public static Task<JsonStructuralIndex> BuildAsync(
		ReadOnlyMemory<byte> jsonBytes,
		int eagerDepthLimit = 1,
		bool mayHaveDuplicateKeys = true,
		CancellationToken cancellationToken = default) =>
		Task.Run(() => Build(jsonBytes.Span, eagerDepthLimit, mayHaveDuplicateKeys, cancellationToken),
			cancellationToken);

	/// <summary>
	///     Indexes the direct children of a container whose child count isn't known yet, records the count
	///     on the container's entry, and returns it.
	/// </summary>
	public int IndexChildren(string parentPath, ReadOnlySpan<byte> jsonBytes, bool mayHaveDuplicateKeys = true)
	{
		if (!_entries.TryGetValue(parentPath, out IndexEntry parentEntry) ||
		    parentEntry.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
		{
			return 0;
		}

		if (parentEntry.ChildCount >= 0)
		{
			return parentEntry.ChildCount;
		}

		ReadOnlySpan<byte> slice = jsonBytes.Slice(
			(int)parentEntry.StartOffset,
			(int)(parentEntry.EndOffset - parentEntry.StartOffset));

		var reader = new Utf8JsonReader(slice, ReaderOptions);
		int count = IndexContainerChildren(ref reader, parentPath, parentEntry.Depth, parentEntry.StartOffset,
			mayHaveDuplicateKeys);

		_entries[parentPath] = parentEntry with { ChildCount = count };
		return count;
	}

	private static JsonStructuralIndex Build(
		ReadOnlySpan<byte> jsonBytes,
		int eagerDepthLimit,
		bool mayHaveDuplicateKeys,
		CancellationToken cancellationToken)
	{
		var index = new JsonStructuralIndex();
		var reader = new Utf8JsonReader(jsonBytes, ReaderOptions);

		var containerStack =
			new Stack<(string Path, long StartOffset, int Depth, int ChildCount, JsonValueKind Kind)>();

		// Property names are only decoded when needed (a container's path, or a possible repeated key);
		// decoding every name allocated a string per property.
		ReadOnlySpan<byte> pendingName = default;
		string? pendingEscapedName = null;
		bool hasPendingName = false;
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

					string? name = hasPendingName ? pendingEscapedName ?? Encoding.UTF8.GetString(pendingName) : null;
					hasPendingName = false;

					string path = BuildPath(containerStack, name);
					long startOffset = reader.TokenStartIndex;
					if (mayHaveDuplicateKeys)
					{
						index.ForgetEarlierValue(path);
					}

					IncrementChildCount(containerStack);

					if (currentDepth > eagerDepthLimit)
					{
						// Beyond eager limit: skip the entire subtree
						reader.TrySkip();
						long endOffset = reader.BytesConsumed;

						index._entries[path] = new IndexEntry(
							path, startOffset, endOffset, kind, -1, currentDepth);
					}
					else
					{
						containerStack.Push((path, startOffset, currentDepth, 0, kind));
					}

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

						index._entries[cPath] = new IndexEntry(
							cPath, cStart, endOffset, cKind, cChildCount, cDepth);
					}

					break;
				}

				case JsonTokenType.PropertyName:
				{
					hasPendingName = true;
					if (reader.ValueIsEscaped)
					{
						pendingEscapedName = reader.GetString();
						pendingName = default;
					}
					else
					{
						pendingEscapedName = null;
						pendingName = reader.ValueSpan;
					}

					break;
				}

				default:
				{
					// A primitive replacing an earlier container under the same key must drop that container.
					if (mayHaveDuplicateKeys && hasPendingName)
					{
						string name = pendingEscapedName ?? Encoding.UTF8.GetString(pendingName);
						index.ForgetEarlierValue(BuildPath(containerStack, name));
					}

					hasPendingName = false;
					IncrementChildCount(containerStack);
					break;
				}
			}
		}

		return index;
	}

	private int IndexContainerChildren(
		ref Utf8JsonReader reader,
		string parentPath,
		int parentDepth,
		long baseOffset,
		bool mayHaveDuplicateKeys)
	{
		// Read the opening token
		if (!reader.Read())
		{
			return 0;
		}

		string? currentPropertyName = null;
		int childCount = 0;

		while (reader.Read())
		{
			switch (reader.TokenType)
			{
				case JsonTokenType.EndObject:
				case JsonTokenType.EndArray:
					return childCount;

				case JsonTokenType.PropertyName:
					currentPropertyName = reader.GetString();
					break;

				case JsonTokenType.StartObject:
				case JsonTokenType.StartArray:
				{
					JsonValueKind kind = reader.TokenType == JsonTokenType.StartObject
						? JsonValueKind.Object
						: JsonValueKind.Array;

					string childPath = ChildPath(parentPath, currentPropertyName, childCount);
					long startOffset = baseOffset + reader.TokenStartIndex;

					reader.TrySkip();
					long endOffset = baseOffset + reader.BytesConsumed;

					if (mayHaveDuplicateKeys)
					{
						ForgetEarlierValue(childPath);
					}

					_entries[childPath] = new IndexEntry(
						childPath, startOffset, endOffset, kind, -1, parentDepth + 1);

					currentPropertyName = null;
					childCount++;
					break;
				}

				default:
				{
					if (mayHaveDuplicateKeys && currentPropertyName is not null)
					{
						ForgetEarlierValue(ChildPath(parentPath, currentPropertyName, childCount));
					}

					currentPropertyName = null;
					childCount++;
					break;
				}
			}
		}

		return childCount;
	}

	private static string ChildPath(string parentPath, string? propertyName, int index) =>
		propertyName is not null
			? $"{parentPath}/{JsonPointerHelper.EscapeSegment(propertyName)}"
			: $"{parentPath}/{index}";

	private static void IncrementChildCount(
		Stack<(string Path, long StartOffset, int Depth, int ChildCount, JsonValueKind Kind)> containerStack)
	{
		if (containerStack.Count > 0)
		{
			(string pPath, long pStart, int pDepth, int pCount, JsonValueKind pKind) = containerStack.Pop();
			containerStack.Push((pPath, pStart, pDepth, pCount + 1, pKind));
		}
	}

	/// <summary>
	///     Handles a key that repeats within one object: the later value wins (as with
	///     <see cref="JsonElement.TryGetProperty(string, out JsonElement)" />), so everything indexed for the
	///     earlier one is dropped.
	/// </summary>
	private void ForgetEarlierValue(string path)
	{
		if (!_entries.TryRemove(path, out _))
		{
			return;
		}

		string prefix = path + "/";
		foreach (string key in _entries.Keys)
		{
			if (key.StartsWith(prefix, StringComparison.Ordinal))
			{
				_entries.TryRemove(key, out _);
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

		// Array: the current child count is the element's index (it goes up after the path is built)
		return parentKind == JsonValueKind.Object && propertyName is not null
			? $"{parentPath}/{JsonPointerHelper.EscapeSegment(propertyName)}"
			: $"{parentPath}/{childCount}";
	}

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
