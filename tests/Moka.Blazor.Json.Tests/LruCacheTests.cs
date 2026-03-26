using System.Diagnostics.CodeAnalysis;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class LruCacheTests
{
	[Fact]
	public void Set_And_Get_Returns_Value()
	{
		var cache = new LruCache<string, int>(3);
		cache.Set("a", 1);

		Assert.True(cache.TryGet("a", out int value));
		Assert.Equal(1, value);
	}

	[Fact]
	public void Get_Missing_Key_Returns_False()
	{
		var cache = new LruCache<string, int>(3);
		Assert.False(cache.TryGet("missing", out _));
	}

	[Fact]
	public void Evicts_Least_Recently_Used_When_Over_Capacity()
	{
		var cache = new LruCache<string, int>(2);
		cache.Set("a", 1);
		cache.Set("b", 2);
		cache.Set("c", 3); // should evict "a"

		Assert.False(cache.TryGet("a", out _));
		Assert.True(cache.TryGet("b", out _));
		Assert.True(cache.TryGet("c", out _));
	}

	[Fact]
	public void Access_Refreshes_LRU_Order()
	{
		var cache = new LruCache<string, int>(2);
		cache.Set("a", 1);
		cache.Set("b", 2);

		// Access "a" to make it most-recently-used
		cache.TryGet("a", out _);

		cache.Set("c", 3); // should evict "b" (now LRU), not "a"

		Assert.True(cache.TryGet("a", out _));
		Assert.False(cache.TryGet("b", out _));
		Assert.True(cache.TryGet("c", out _));
	}

	[Fact]
	public void Update_Existing_Key_Preserves_Capacity()
	{
		var cache = new LruCache<string, int>(2);
		cache.Set("a", 1);
		cache.Set("b", 2);
		cache.Set("a", 10); // update, not new entry

		Assert.Equal(2, cache.Count);
		Assert.True(cache.TryGet("a", out int value));
		Assert.Equal(10, value);
	}

	[Fact]
	public void Clear_Removes_All_Entries()
	{
		var cache = new LruCache<string, int>(3);
		cache.Set("a", 1);
		cache.Set("b", 2);

		cache.Clear();

		Assert.Equal(0, cache.Count);
		Assert.False(cache.TryGet("a", out _));
	}

	[Fact]
	[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "Testing that cache does NOT dispose evicted values")]
	public void Eviction_Does_Not_Dispose_Values()
	{
		using var cache = new DisposableCacheWrapper<string, DisposableValue>(1);
		var v1 = new DisposableValue();
		cache.Inner.Set("a", v1);

		var v2 = new DisposableValue();
		cache.Inner.Set("b", v2); // evicts v1 but does NOT dispose it

		// Evicted values are not disposed — concurrent readers may still hold references.
		Assert.False(v1.IsDisposed);
		Assert.False(v2.IsDisposed);
	}

	[Fact]
	[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "Testing that cache disposes values")]
	public void Clear_Disposes_All_IDisposable_Values()
	{
		var cache = new LruCache<string, DisposableValue>(3);
		var v1 = new DisposableValue();
		var v2 = new DisposableValue();
		cache.Set("a", v1);
		cache.Set("b", v2);

		cache.Clear(); // disposes v1 and v2

		Assert.True(v1.IsDisposed);
		Assert.True(v2.IsDisposed);
	}

	private sealed class DisposableValue : IDisposable
	{
		public bool IsDisposed { get; private set; }
		public void Dispose() => IsDisposed = true;
	}

	private sealed class DisposableCacheWrapper<TKey, TValue>(int capacity) : IDisposable where TKey : notnull
	{
		public LruCache<TKey, TValue> Inner { get; } = new(capacity);
		public void Dispose() => Inner.Clear();
	}
}
