namespace Moka.Blazor.Json.Models;

/// <summary>
///     Debug statistics for lazy JSON document source.
///     Used by Moka.Blazor.Json.Diagnostics to render the debug overlay.
/// </summary>
public sealed class LazyDebugStats
{
	private readonly Lock _lock = new();
	private readonly List<ParsedRegion> _parsedRegions = [];
	private int _cacheHits;
	private int _cacheMisses;
	private int _lazyIndexOps;

	/// <summary>Total document size in bytes.</summary>
	public long TotalBytes { get; set; }

	/// <summary>Number of entries in the structural index.</summary>
	public int IndexEntries { get; set; }

	/// <summary>Total number of subtree parse operations (including overlapping regions).</summary>
	public int SubtreeParseCount { get; private set; }

	/// <summary>Cumulative bytes parsed across all parse calls (may double-count overlapping regions).</summary>
	public long CumulativeBytesParsed { get; private set; }

	/// <summary>Unique bytes covered by at least one parse (no double-counting).</summary>
	public long UniqueBytesParsed { get; private set; }

	/// <summary>Number of times a cached subtree was reused.</summary>
	public int CacheHits => _cacheHits;

	/// <summary>Number of times a subtree had to be parsed fresh (cache miss).</summary>
	public int CacheMisses => _cacheMisses;

	/// <summary>Number of lazy child-indexing operations.</summary>
	public int LazyIndexOps => _lazyIndexOps;

	/// <summary>Total time spent parsing subtrees.</summary>
	public TimeSpan TotalParseTime { get; private set; }

	/// <summary>Fastest subtree parse.</summary>
	public TimeSpan MinParseTime { get; private set; } = TimeSpan.MaxValue;

	/// <summary>Slowest subtree parse.</summary>
	public TimeSpan MaxParseTime { get; private set; }

	/// <summary>Average subtree parse time.</summary>
	public TimeSpan AvgParseTime => SubtreeParseCount > 0
		? TotalParseTime / SubtreeParseCount
		: TimeSpan.Zero;

	/// <summary>Percentage of unique document bytes that have been parsed.</summary>
	public double CoveragePercent => TotalBytes > 0 ? (double)UniqueBytesParsed / TotalBytes * 100 : 0;

	/// <summary>Cache hit rate as a percentage.</summary>
	public double CacheHitRate
	{
		get
		{
			int total = _cacheHits + _cacheMisses;
			return total > 0 ? (double)_cacheHits / total * 100 : 0;
		}
	}

	/// <summary>Recent parse operations with path, size, and duration.</summary>
	public List<LazyParseEntry> RecentParses { get; } = new(20);

	/// <summary>Records a subtree parse operation.</summary>
	public void RecordParse(string path, long startOffset, long length, TimeSpan duration)
	{
		lock (_lock)
		{
			SubtreeParseCount++;
			CumulativeBytesParsed += length;
			TotalParseTime += duration;

			if (duration < MinParseTime)
			{
				MinParseTime = duration;
			}

			if (duration > MaxParseTime)
			{
				MaxParseTime = duration;
			}

			_parsedRegions.Add(new ParsedRegion(startOffset, startOffset + length));
			RecalculateUniqueCoverage();

			if (RecentParses.Count >= 20)
			{
				RecentParses.RemoveAt(0);
			}

			RecentParses.Add(new LazyParseEntry(path, length, duration));
		}
	}

	/// <summary>Records a cache hit.</summary>
	public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);

	/// <summary>Records a cache miss.</summary>
	public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

	/// <summary>Records a lazy index operation.</summary>
	public void RecordLazyIndex() => Interlocked.Increment(ref _lazyIndexOps);

	private void RecalculateUniqueCoverage()
	{
		if (_parsedRegions.Count == 0)
		{
			UniqueBytesParsed = 0;
			return;
		}

		var sorted = _parsedRegions.OrderBy(r => r.Start).ToList();
		long uniqueBytes = 0;
		long mergedStart = sorted[0].Start;
		long mergedEnd = sorted[0].End;

		for (int i = 1; i < sorted.Count; i++)
		{
			if (sorted[i].Start <= mergedEnd)
			{
				mergedEnd = Math.Max(mergedEnd, sorted[i].End);
			}
			else
			{
				uniqueBytes += mergedEnd - mergedStart;
				mergedStart = sorted[i].Start;
				mergedEnd = sorted[i].End;
			}
		}

		uniqueBytes += mergedEnd - mergedStart;
		UniqueBytesParsed = uniqueBytes;
	}

	private readonly record struct ParsedRegion(long Start, long End);
}

/// <summary>A single parse operation entry for diagnostics.</summary>
public sealed record LazyParseEntry(string Path, long Bytes, TimeSpan Duration)
{
	/// <inheritdoc />
	public override string ToString()
	{
		string size = Bytes switch
		{
			< 1024 => $"{Bytes} B",
			< 1024 * 1024 => $"{Bytes / 1024.0:F1} KB",
			_ => $"{Bytes / (1024.0 * 1024):F1} MB"
		};
		return $"{Path} ({size}, {Duration.TotalMilliseconds:F1}ms)";
	}
}
