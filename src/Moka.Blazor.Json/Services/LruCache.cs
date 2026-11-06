namespace Moka.Blazor.Json.Services;

/// <summary>
///     A simple LRU (Least Recently Used) cache with a fixed capacity.
///     Evicted values are disposed if they implement <see cref="IDisposable" />, unless
///     <c>disposeOnEvict</c> is false.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
	private readonly int _capacity;
	private readonly bool _disposeOnEvict;
	private readonly LinkedList<(TKey Key, TValue Value)> _list = [];
	private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
	private readonly Func<TValue, long>? _weigh;
	private readonly long _maxWeight;
	private long _weight;

	// The newest entry is always kept, however much it weighs.
	public LruCache(int capacity, bool disposeOnEvict = true, Func<TValue, long>? weigh = null,
		long maxWeight = long.MaxValue)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		_capacity = capacity;
		_disposeOnEvict = disposeOnEvict;
		_weigh = weigh;
		_maxWeight = maxWeight;
		_map = new Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>>(Math.Min(capacity, 1024));
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
			_weight += Weigh(value) - Weigh(existing.Value.Value);
			existing.Value = (key, value);
			_list.Remove(existing);
			_list.AddFirst(existing);
			EvictOverWeight();
			return;
		}

		if (_map.Count >= _capacity)
		{
			EvictLast();
		}

		var node = new LinkedListNode<(TKey Key, TValue Value)>((key, value));
		_list.AddFirst(node);
		_map[key] = node;
		_weight += Weigh(value);
		EvictOverWeight();
	}

	private long Weigh(TValue value) => _weigh?.Invoke(value) ?? 0;

	private void EvictOverWeight()
	{
		while (_weight > _maxWeight && _map.Count > 1)
		{
			EvictLast();
		}
	}

	private void EvictLast()
	{
		LinkedListNode<(TKey Key, TValue Value)>? last = _list.Last;
		if (last is null)
		{
			return;
		}

		if (_disposeOnEvict)
		{
			DisposeValue(last.Value.Value);
		}

		_weight -= Weigh(last.Value.Value);
		_list.RemoveLast();
		_map.Remove(last.Value.Key);
	}

	public void Clear()
	{
		_weight = 0;
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
