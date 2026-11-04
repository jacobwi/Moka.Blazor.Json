using System.Text.Json;
using System.Text.RegularExpressions;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Provides search functionality across a JSON document, supporting
///     plain text, regex, and JSON Path queries.
/// </summary>
/// <remarks>
///     <see cref="FindMatches" /> is safe to run on a background thread: it builds a new result list and
///     touches no navigation state. Apply the result on the UI thread with <see cref="SetMatches" />.
/// </remarks>
internal sealed class JsonSearchEngine
{
	#region Nested Types

	private sealed record RegexCacheEntry(string Pattern, RegexOptions Options, Regex Regex);

	private sealed class TextMatcher(string query, StringComparison comparison, Regex? regex)
	{
		public bool IsMatch(string text) => regex?.IsMatch(text) ?? text.Contains(query, comparison);
	}

	private sealed class MatchCollector
	{
		private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

		public List<string> Paths { get; } = [];

		public void Add(string path)
		{
			if (_seen.Add(path))
			{
				Paths.Add(path);
			}
		}
	}

	#endregion

	#region Fields

	private IReadOnlyList<string> _matchPaths = [];
	private RegexCacheEntry? _regexCache;

	#endregion

	#region Properties

	/// <summary>
	///     Gets the paths of all current search matches.
	/// </summary>
	public IReadOnlyList<string> MatchPaths => _matchPaths;

	/// <summary>
	///     Gets the index of the currently active match, or -1 if none.
	/// </summary>
	public int ActiveMatchIndex { get; private set; } = -1;

	/// <summary>
	///     Gets the path of the currently active match, or null.
	/// </summary>
	public string? ActiveMatchPath
	{
		get
		{
			IReadOnlyList<string> matches = _matchPaths;
			int index = ActiveMatchIndex;
			return index >= 0 && index < matches.Count ? matches[index] : null;
		}
	}

	/// <summary>
	///     Gets the total number of matches found.
	/// </summary>
	public int MatchCount => _matchPaths.Count;

	#endregion

	#region Public Methods

	/// <summary>
	///     Executes a search across the document and returns the number of matches.
	/// </summary>
	/// <param name="root">The root element to search.</param>
	/// <param name="query">The search query.</param>
	/// <param name="options">Search options.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The number of matches found.</returns>
	public int Search(JsonElement root, string query, JsonSearchOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		Clear();
		options ??= new JsonSearchOptions();

		if (string.IsNullOrEmpty(query) || !TryCreateMatcher(query, options, out TextMatcher? matcher))
		{
			return 0;
		}

		var collector = new MatchCollector();
		try
		{
			SearchElement(root, "", matcher, options, collector, cancellationToken);
		}
		catch (RegexMatchTimeoutException)
		{
			return 0;
		}

		return SetMatches(collector.Paths);
	}

	/// <summary>
	///     Executes a search across the document via an <see cref="IJsonDocumentSource" />.
	/// </summary>
	public int Search(IJsonDocumentSource source, string query, JsonSearchOptions? options = null,
		CancellationToken cancellationToken = default) =>
		SetMatches(FindMatches(source, "", query, options, cancellationToken));

	/// <summary>
	///     Finds all matches under <paramref name="rootPath" /> without changing the engine's current results.
	/// </summary>
	public IReadOnlyList<string> FindMatches(IJsonDocumentSource source, string rootPath, string query,
		JsonSearchOptions? options = null, CancellationToken cancellationToken = default)
	{
		options ??= new JsonSearchOptions();

		if (string.IsNullOrEmpty(query) || !TryCreateMatcher(query, options, out TextMatcher? matcher))
		{
			return [];
		}

		var collector = new MatchCollector();
		try
		{
			if (source.GetValueKind(rootPath) is JsonValueKind.Object or JsonValueKind.Array)
			{
				SearchSource(source, rootPath, matcher, options, collector, cancellationToken);
			}
		}
		catch (RegexMatchTimeoutException)
		{
			return [];
		}

		return collector.Paths;
	}

	/// <summary>
	///     Replaces the current results and makes the first match active.
	/// </summary>
	public int SetMatches(IReadOnlyList<string> matchPaths)
	{
		_matchPaths = matchPaths;
		ActiveMatchIndex = matchPaths.Count > 0 ? 0 : -1;
		return matchPaths.Count;
	}

	/// <summary>
	///     Moves to the next match and returns its path.
	/// </summary>
	public string? NextMatch()
	{
		IReadOnlyList<string> matches = _matchPaths;
		if (matches.Count == 0)
		{
			return null;
		}

		ActiveMatchIndex = (ActiveMatchIndex + 1) % matches.Count;
		return matches[ActiveMatchIndex];
	}

	/// <summary>
	///     Moves to the previous match and returns its path.
	/// </summary>
	public string? PreviousMatch()
	{
		IReadOnlyList<string> matches = _matchPaths;
		if (matches.Count == 0)
		{
			return null;
		}

		ActiveMatchIndex = (ActiveMatchIndex - 1 + matches.Count) % matches.Count;
		return matches[ActiveMatchIndex];
	}

	/// <summary>
	///     Clears all search state.
	/// </summary>
	public void Clear()
	{
		_matchPaths = [];
		ActiveMatchIndex = -1;
	}

	/// <summary>
	///     Executes an async streaming search over raw JSON bytes.
	///     Designed for large documents where DOM-based search is too expensive.
	/// </summary>
	public async Task<int> SearchStreamingAsync(
		ReadOnlyMemory<byte> jsonBytes,
		string query,
		JsonSearchOptions? options = null,
		IProgress<StreamingJsonSearcher.SearchProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		Clear();

		if (string.IsNullOrEmpty(query))
		{
			return 0;
		}

		List<string> results = await StreamingJsonSearcher.SearchAsync(
			jsonBytes, query, options, progress, cancellationToken).ConfigureAwait(false);

		var collector = new MatchCollector();
		foreach (string path in results)
		{
			collector.Add(path);
		}

		return SetMatches(collector.Paths);
	}

	#endregion

	#region Private Methods

	private bool TryCreateMatcher(string query, JsonSearchOptions options,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TextMatcher? matcher)
	{
		StringComparison comparison = options.CaseSensitive
			? StringComparison.Ordinal
			: StringComparison.OrdinalIgnoreCase;

		if (!options.UseRegex)
		{
			matcher = new TextMatcher(query, comparison, null);
			return true;
		}

		RegexOptions regexOptions = RegexOptions.Compiled;
		if (!options.CaseSensitive)
		{
			regexOptions |= RegexOptions.IgnoreCase;
		}

		// Read once: searches for successive keystrokes can run concurrently on the thread pool.
		RegexCacheEntry? cached = _regexCache;
		if (cached is null || cached.Pattern != query || cached.Options != regexOptions)
		{
			try
			{
				cached = new RegexCacheEntry(query, regexOptions,
					new Regex(query, regexOptions, TimeSpan.FromSeconds(1)));
			}
			catch (ArgumentException)
			{
				matcher = null;
				return false;
			}

			_regexCache = cached;
		}

		matcher = new TextMatcher(query, comparison, cached.Regex);
		return true;
	}

	private static void SearchElement(
		JsonElement element,
		string path,
		TextMatcher matcher,
		JsonSearchOptions options,
		MatchCollector collector,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (KeyValuePair<string, JsonElement> prop in JsonObjectReader.GetProperties(element))
				{
					string childPath = $"{path}/{JsonPointerHelper.EscapeSegment(prop.Key)}";

					if (options.SearchKeys && matcher.IsMatch(prop.Key))
					{
						collector.Add(childPath);
					}

					MatchElementValue(prop.Value, childPath, matcher, options, collector);
					SearchElement(prop.Value, childPath, matcher, options, collector, cancellationToken);
				}

				break;

			case JsonValueKind.Array:
				int i = 0;
				foreach (JsonElement item in element.EnumerateArray())
				{
					string childPath = $"{path}/{i}";
					MatchElementValue(item, childPath, matcher, options, collector);
					SearchElement(item, childPath, matcher, options, collector, cancellationToken);
					i++;
				}

				break;
		}
	}

	private static void MatchElementValue(
		JsonElement element,
		string path,
		TextMatcher matcher,
		JsonSearchOptions options,
		MatchCollector collector)
	{
		if (!options.SearchValues || element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
		{
			return;
		}

		string valueText = element.ValueKind == JsonValueKind.String
			? element.GetString() ?? ""
			: element.GetRawText();

		if (matcher.IsMatch(valueText))
		{
			collector.Add(path);
		}
	}

	private static void SearchSource(
		IJsonDocumentSource source,
		string path,
		TextMatcher matcher,
		JsonSearchOptions options,
		MatchCollector collector,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		foreach (JsonChildDescriptor child in source.EnumerateChildren(path))
		{
			if (options.SearchKeys && child.PropertyName is not null && matcher.IsMatch(child.PropertyName))
			{
				collector.Add(child.Path);
			}

			if (options.SearchValues && child.RawValue is not null && matcher.IsMatch(child.RawValue))
			{
				collector.Add(child.Path);
			}

			if (child.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				SearchSource(source, child.Path, matcher, options, collector, cancellationToken);
			}
		}
	}

	#endregion
}
