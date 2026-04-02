namespace Moka.Blazor.Json.Services;

/// <summary>
///     A simple LRU (Least Recently Used) cache with a fixed capacity.
///     Evicted values are disposed if they implement <see cref="IDisposable" />.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
	private readonly int _capacity;
	private readonly LinkedList<(TKey Key, TValue Value)> _list = [];
	private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;

	public LruCache(int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		_capacity = capacity;
		_map = new Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>>(capacity);
	}

	public int Count => _map.Count;

	public bool TryGet(TKey key, out TValue value)
	{
		if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? node))
		{
			// Move to front (most recently used)
			_list.Remove(node);
			_list.AddFirst(node);
			value = node.Value.Value;
			return true;
		}

		value = default!;
		return false;
	}

	public void Set(TKey key, TValue value)
	{
		if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? existing))
		{
			// Update existing: set new value, move to front.
			// Don't dispose the old value — a concurrent Task.Run may still be reading it.
			// The GC will reclaim it once no references remain.
			existing.Value = (key, value);
			_list.Remove(existing);
			_list.AddFirst(existing);
			return;
		}

		// Evict if at capacity — dispose the evicted value since no callers hold references
		// to values they haven't already retrieved via TryGet.
		if (_map.Count >= _capacity)
		{
			LinkedListNode<(TKey Key, TValue Value)>? last = _list.Last;
			if (last is not null)
			{
				DisposeValue(last.Value.Value);
				_list.RemoveLast();
				_map.Remove(last.Value.Key);
			}
		}

		var node = new LinkedListNode<(TKey Key, TValue Value)>((key, value));
		_list.AddFirst(node);
		_map[key] = node;
	}

	public void Clear()
	{
		LinkedListNode<(TKey Key, TValue Value)>? node = _list.First;
		while (node is not null)
		{
			DisposeValue(node.Value.Value);
			node = node.Next;
		}

		_list.Clear();
		_map.Clear();
	}

	private static void DisposeValue(TValue value)
	{
		if (value is IDisposable disposable)
		{
			disposable.Dispose();
		}
	}
}
