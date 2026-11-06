using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

// Each limit trades index memory against how much of the document a lookup has to read.
internal sealed record JsonIndexLimits
{
	public static readonly JsonIndexLimits Default = new();

	public int BlockBytes { get; init; } = OperatingSystem.IsBrowser() ? 256 * 1024 : 4 * 1024 * 1024;

	// Containers at least this big are never parsed whole for browsing. Anything smaller is parsed on demand,
	// which takes a few milliseconds.
	public long IndexedContainerBytes { get; init; } = 4 * 1024 * 1024;

	// A container only keeps a list of its children if they're big enough on average that reading them back
	// would be slow. Otherwise a list could approach the size of the document.
	public int MaxListedChildren { get; init; } = 256 * 1024;

	public int AlwaysListedChildren { get; init; } = 64;

	public int MinBytesPerListedChild { get; init; } = 4096;

	// Must not exceed IndexedContainerBytes, so that the child after an indexed child always has a checkpoint.
	public long CheckpointBytes { get; init; } = 1024 * 1024;

	public int CheckpointChildren { get; init; } = 4096;

	public long MaxMaterializedBytes { get; init; } = 1024L * 1024 * 1024;
}

internal readonly record struct IndexedChild(string? Name, long Start, long End, JsonValueKind Kind, int ChildCount)
{
	public bool IsContainer => Kind is JsonValueKind.Object or JsonValueKind.Array;
}

// A container too big to parse whole. Its children are either listed, or found by reading forward from the
// nearest checkpoint.
internal sealed class IndexedContainer
{
	private const int LinearNameScanLimit = 16;

	private Dictionary<string, int>? _namePositions;

	public required long Start { get; init; }

	public required long End { get; init; }

	public required JsonValueKind Kind { get; init; }

	public required int ChildCount { get; init; }

	public IndexedChild[]? Children { get; init; }

	public long[] CheckpointOffsets { get; init; } = [];

	public int[] CheckpointIndexes { get; init; } = [];

	public int IndexOfName(string name)
	{
		IndexedChild[] children = Children!;
		if (children.Length <= LinearNameScanLimit)
		{
			for (int i = 0; i < children.Length; i++)
			{
				if (string.Equals(children[i].Name, name, StringComparison.Ordinal))
				{
					return i;
				}
			}

			return -1;
		}

		Dictionary<string, int>? positions = _namePositions;
		if (positions is null)
		{
			positions = new Dictionary<string, int>(children.Length, StringComparer.Ordinal);
			for (int i = 0; i < children.Length; i++)
			{
				positions.TryAdd(children[i].Name!, i);
			}

			_namePositions = positions;
		}

		return positions.TryGetValue(name, out int position) ? position : -1;
	}

	public int FindCheckpoint(int childIndex)
	{
		int position = Array.BinarySearch(CheckpointIndexes, childIndex);
		return position >= 0 ? position : ~position - 1;
	}
}

// How big a container is isn't known until it closes, so every open container collects its children and hands
// them to the index only if it turns out to be big. Everything else is forgotten on close, which keeps the pass
// free of allocations and leaves very few entries, even for a document of many GB.
internal sealed class JsonStructuralIndex
{
	private readonly Dictionary<long, IndexedContainer> _containers = [];

	private JsonStructuralIndex()
	{
	}

	public IndexedContainer? Root { get; private set; }

	public IndexedChild RootValue { get; private set; }

	public long NodeCount { get; private set; }

	public int MaxDepth { get; private set; }

	public bool MayHaveDuplicateKeys { get; private set; }

	public int EntryCount => _containers.Count;

	public bool TryGetContainer(long start, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IndexedContainer? container) =>
		_containers.TryGetValue(start, out container);

	public static async Task<JsonStructuralIndex> BuildAsync(
		IJsonByteSource bytes,
		long start = 0,
		JsonIndexLimits? limits = null,
		Action<long>? progress = null,
		CancellationToken cancellationToken = default)
	{
		limits ??= JsonIndexLimits.Default;
		var builder = new Builder(bytes, limits);
		using var feeder = new JsonBlockFeeder(bytes, start, bytes.Length, limits.BlockBytes);

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			int consumed = builder.ProcessBlock(feeder.Window.Span, feeder.IsFinal, feeder.WindowOffset);
			if (feeder.IsFinal)
			{
				return builder.Index;
			}

			feeder.Advance(consumed);
			progress?.Invoke(feeder.WindowOffset);
			await BrowserThread.YieldAsync().ConfigureAwait(false);
		}
	}

	private sealed class Builder(IJsonByteSource bytes, JsonIndexLimits limits)
	{
		private readonly List<Frame> _frames = [];
		private JsonReaderState _state = new(JsonFormatting.LenientReader);
		private int _depth = -1;

		public JsonStructuralIndex Index { get; } = new();

		public int ProcessBlock(ReadOnlySpan<byte> window, bool isFinal, long windowOffset)
		{
			var reader = new Utf8JsonReader(window, isFinal, _state);

			while (reader.Read())
			{
				switch (reader.TokenType)
				{
					case JsonTokenType.StartObject:
						Open(windowOffset + reader.TokenStartIndex, JsonValueKind.Object);
						break;

					case JsonTokenType.StartArray:
						Open(windowOffset + reader.TokenStartIndex, JsonValueKind.Array);
						break;

					case JsonTokenType.EndObject:
					case JsonTokenType.EndArray:
						Close(windowOffset + reader.BytesConsumed);
						break;

					case JsonTokenType.PropertyName:
						RecordName(ref reader, windowOffset);
						break;

					default:
						RecordPrimitive(reader.TokenType, windowOffset + reader.TokenStartIndex,
							windowOffset + reader.BytesConsumed);
						break;
				}
			}

			_state = reader.CurrentState;
			return (int)reader.BytesConsumed;
		}

		private void Open(long start, JsonValueKind kind)
		{
			_depth++;
			if (_depth == _frames.Count)
			{
				_frames.Add(new Frame());
			}

			_frames[_depth].Reset(start, kind);
			CountNode(_depth);
		}

		private void Close(long end)
		{
			Frame frame = _frames[_depth--];
			bool isRoot = _depth < 0;

			if (isRoot || end - frame.Start >= limits.IndexedContainerBytes)
			{
				IndexedContainer container = frame.ToContainer(end, bytes, limits, Index.MayHaveDuplicateKeys);
				Index._containers[frame.Start] = container;
				if (isRoot)
				{
					Index.Root = container;
				}
			}

			if (isRoot)
			{
				Index.RootValue = new IndexedChild(null, frame.Start, end, frame.Kind, frame.ChildCount);
			}
			else
			{
				_frames[_depth].AddChild(frame.Kind, frame.Start, end, frame.ChildCount, limits);
			}
		}

		private void RecordName(ref Utf8JsonReader reader, long windowOffset)
		{
			Frame frame = _frames[_depth];
			frame.PendingNameStart = windowOffset + reader.TokenStartIndex;
			frame.PendingNameLength = reader.ValueSpan.Length + 2;

			if (Index.MayHaveDuplicateKeys)
			{
				return;
			}

			ulong hash = reader.ValueIsEscaped
				? JsonObjectReader.HashBytes(Encoding.UTF8.GetBytes(reader.GetString() ?? ""))
				: JsonObjectReader.HashBytes(reader.ValueSpan);

			if (!frame.TryAddName(hash))
			{
				Index.MayHaveDuplicateKeys = true;
			}
		}

		private void RecordPrimitive(JsonTokenType token, long start, long end)
		{
			JsonValueKind kind = token switch
			{
				JsonTokenType.String => JsonValueKind.String,
				JsonTokenType.Number => JsonValueKind.Number,
				JsonTokenType.True => JsonValueKind.True,
				JsonTokenType.False => JsonValueKind.False,
				_ => JsonValueKind.Null
			};

			CountNode(_depth + 1);

			if (_depth < 0)
			{
				Index.RootValue = new IndexedChild(null, start, end, kind, 0);
			}
			else
			{
				_frames[_depth].AddChild(kind, start, end, 0, limits);
			}
		}

		private void CountNode(int depth)
		{
			Index.NodeCount++;
			if (depth > Index.MaxDepth)
			{
				Index.MaxDepth = depth;
			}
		}
	}

	// Frames are reused per depth, so a pass over millions of containers allocates almost nothing.
	private sealed class Frame
	{
		private const int LinearHashLimit = 16;

		private readonly ulong[] _nameHashes = new ulong[LinearHashLimit];
		private readonly List<long> _checkpointOffsets = [];
		private readonly List<int> _checkpointIndexes = [];
		private ChildRecord[] _children = new ChildRecord[16];
		private HashSet<ulong>? _manyNameHashes;
		private int _nameCount;
		private bool _usesCheckpoints;

		public long Start { get; private set; }

		public JsonValueKind Kind { get; private set; }

		public int ChildCount { get; private set; }

		public long PendingNameStart { get; set; }

		public int PendingNameLength { get; set; }

		public void Reset(long start, JsonValueKind kind)
		{
			Start = start;
			Kind = kind;
			ChildCount = 0;
			PendingNameStart = 0;
			PendingNameLength = 0;
			_usesCheckpoints = false;
			_nameCount = 0;
			_manyNameHashes?.Clear();
			_checkpointOffsets.Clear();
			_checkpointIndexes.Clear();
		}

		public bool TryAddName(ulong hash)
		{
			if (_nameCount < LinearHashLimit)
			{
				for (int i = 0; i < _nameCount; i++)
				{
					if (_nameHashes[i] == hash)
					{
						return false;
					}
				}

				_nameHashes[_nameCount++] = hash;
				return true;
			}

			if (_manyNameHashes is null || _manyNameHashes.Count == 0)
			{
				_manyNameHashes ??= [];
				foreach (ulong existing in _nameHashes)
				{
					_manyNameHashes.Add(existing);
				}
			}

			return _manyNameHashes.Add(hash);
		}

		public void AddChild(JsonValueKind kind, long start, long end, int childCount, JsonIndexLimits limits)
		{
			int index = ChildCount++;

			if (_usesCheckpoints)
			{
				AddCheckpointIfDue(EntryStart(start, PendingNameStart), index, limits);
				return;
			}

			if (index == _children.Length)
			{
				Array.Resize(ref _children, _children.Length * 2);
			}

			_children[index] = new ChildRecord(PendingNameStart, PendingNameLength, start, end, childCount, kind);

			if (ChildCount > limits.MaxListedChildren)
			{
				SwitchToCheckpoints(limits);
			}
		}

		public IndexedContainer ToContainer(long end, IJsonByteSource bytes, JsonIndexLimits limits, bool dedupe)
		{
			bool list = !_usesCheckpoints &&
			            (ChildCount <= limits.AlwaysListedChildren ||
			             end - Start >= ChildCount * (long)limits.MinBytesPerListedChild);

			if (list)
			{
				IndexedChild[] children = ReadChildList(bytes, dedupe);
				return new IndexedContainer
				{
					Start = Start,
					End = end,
					Kind = Kind,
					ChildCount = children.Length,
					Children = children
				};
			}

			if (!_usesCheckpoints)
			{
				SwitchToCheckpoints(limits);
			}

			return new IndexedContainer
			{
				Start = Start,
				End = end,
				Kind = Kind,
				ChildCount = ChildCount,
				CheckpointOffsets = [.._checkpointOffsets],
				CheckpointIndexes = [.._checkpointIndexes]
			};
		}

		private long EntryStart(long valueStart, long nameStart) => Kind == JsonValueKind.Object ? nameStart : valueStart;

		private void SwitchToCheckpoints(JsonIndexLimits limits)
		{
			_usesCheckpoints = true;
			for (int i = 0; i < ChildCount; i++)
			{
				AddCheckpointIfDue(EntryStart(_children[i].Start, _children[i].NameStart), i, limits);
			}
		}

		private void AddCheckpointIfDue(long entryStart, int index, JsonIndexLimits limits)
		{
			if (_checkpointOffsets.Count == 0 ||
			    entryStart - _checkpointOffsets[^1] >= limits.CheckpointBytes ||
			    index - _checkpointIndexes[^1] >= limits.CheckpointChildren)
			{
				_checkpointOffsets.Add(entryStart);
				_checkpointIndexes.Add(index);
			}
		}

		private IndexedChild[] ReadChildList(IJsonByteSource bytes, bool dedupe)
		{
			var children = new IndexedChild[ChildCount];
			for (int i = 0; i < ChildCount; i++)
			{
				ref readonly ChildRecord record = ref _children[i];
				string? name = Kind == JsonValueKind.Object ? ReadName(bytes, record.NameStart, record.NameLength) : null;
				children[i] = new IndexedChild(name, record.Start, record.End, record.Kind, record.ChildCount);
			}

			return dedupe && Kind == JsonValueKind.Object ? KeepLastValueOfEachName(children) : children;
		}

		// A repeated key shows once, in its first position, with its last value.
		private static IndexedChild[] KeepLastValueOfEachName(IndexedChild[] children)
		{
			var positions = new Dictionary<string, int>(children.Length, StringComparer.Ordinal);
			var unique = new List<IndexedChild>(children.Length);
			foreach (IndexedChild child in children)
			{
				if (positions.TryGetValue(child.Name!, out int position))
				{
					unique[position] = child;
				}
				else
				{
					positions.Add(child.Name!, unique.Count);
					unique.Add(child);
				}
			}

			return [..unique];
		}

		private static string ReadName(IJsonByteSource bytes, long start, int length)
		{
			ReadOnlySpan<byte> quoted = bytes.GetMemory(start, length).Span;
			if (quoted.IndexOf((byte)'\\') < 0)
			{
				return Encoding.UTF8.GetString(quoted[1..^1]);
			}

			var reader = new Utf8JsonReader(quoted);
			reader.Read();
			Debug.Assert(reader.TokenType == JsonTokenType.String);
			return reader.GetString() ?? "";
		}
	}

	private readonly record struct ChildRecord(
		long NameStart,
		int NameLength,
		long Start,
		long End,
		int ChildCount,
		JsonValueKind Kind);
}
