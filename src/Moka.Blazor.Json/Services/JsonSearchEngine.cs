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
internal sealed class JsonSearchEngine
{
	#region Fields

	private readonly List<string> _matchPaths = [];
	private readonly HashSet<string> _matchPathSet = [];

	private string? _cachedRegexPattern;
	private RegexOptions _cachedRegexOptions;
	private Regex? _cachedRegex;

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
	public string? ActiveMatchPath => ActiveMatchIndex >= 0 && ActiveMatchIndex < _matchPaths.Count
		? _matchPaths[ActiveMatchIndex]
		: null;

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
		_matchPaths.Clear();
		_matchPathSet.Clear();
		ActiveMatchIndex = -1;

		if (string.IsNullOrEmpty(query))
		{
			return 0;
		}

		options ??= new JsonSearchOptions();

		if (options.UseRegex)
		{
			RegexOptions regexOptions = RegexOptions.Compiled;
			if (!options.CaseSensitive)
			{
				regexOptions |= RegexOptions.IgnoreCase;
			}

			// Cache compiled regex — reuse if pattern and options haven't changed
			if (_cachedRegex is null || _cachedRegexPattern != query || _cachedRegexOptions != regexOptions)
			{
				try
				{
					_cachedRegex = new Regex(query, regexOptions, TimeSpan.FromSeconds(1));
					_cachedRegexPattern = query;
					_cachedRegexOptions = regexOptions;
				}
				catch (RegexParseException)
				{
					return 0;
				}
			}

			SearchWithRegex(root, "", _cachedRegex, options, cancellationToken);
		}
		else
		{
			StringComparison comparison = options.CaseSensitive
				? StringComparison.Ordinal
				: StringComparison.OrdinalIgnoreCase;
			SearchPlainText(root, "", query, comparison, options, cancellationToken);
		}

		if (_matchPaths.Count > 0)
		{
			ActiveMatchIndex = 0;
		}

		return _matchPaths.Count;
	}

	/// <summary>
	///     Moves to the next match and returns its path.
	/// </summary>
	public string? NextMatch()
	{
		if (_matchPaths.Count == 0)
		{
			return null;
		}

		ActiveMatchIndex = (ActiveMatchIndex + 1) % _matchPaths.Count;
		return _matchPaths[ActiveMatchIndex];
	}

	/// <summary>
	///     Moves to the previous match and returns its path.
	/// </summary>
	public string? PreviousMatch()
	{
		if (_matchPaths.Count == 0)
		{
			return null;
		}

		ActiveMatchIndex = (ActiveMatchIndex - 1 + _matchPaths.Count) % _matchPaths.Count;
		return _matchPaths[ActiveMatchIndex];
	}

	/// <summary>
	///     Clears all search state.
	/// </summary>
	public void Clear()
	{
		_matchPaths.Clear();
		_matchPathSet.Clear();
		ActiveMatchIndex = -1;
	}

	/// <summary>
	///     Executes a search across the document via an <see cref="IJsonDocumentSource" />.
	/// </summary>
	public int Search(IJsonDocumentSource source, string query, JsonSearchOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		_matchPaths.Clear();
		_matchPathSet.Clear();
		ActiveMatchIndex = -1;

		if (string.IsNullOrEmpty(query))
		{
			return 0;
		}

		options ??= new JsonSearchOptions();

		if (options.UseRegex)
		{
			RegexOptions regexOptions = RegexOptions.Compiled;
			if (!options.CaseSensitive)
			{
				regexOptions |= RegexOptions.IgnoreCase;
			}

			if (_cachedRegex is null || _cachedRegexPattern != query || _cachedRegexOptions != regexOptions)
			{
				try
				{
					_cachedRegex = new Regex(query, regexOptions, TimeSpan.FromSeconds(1));
					_cachedRegexPattern = query;
					_cachedRegexOptions = regexOptions;
				}
				catch (RegexParseException)
				{
					return 0;
				}
			}

			SearchWithRegex(source, "", _cachedRegex, options, cancellationToken);
		}
		else
		{
			StringComparison comparison = options.CaseSensitive
				? StringComparison.Ordinal
				: StringComparison.OrdinalIgnoreCase;
			SearchPlainText(source, "", query, comparison, options, cancellationToken);
		}

		if (_matchPaths.Count > 0)
		{
			ActiveMatchIndex = 0;
		}

		return _matchPaths.Count;
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
		_matchPaths.Clear();
		_matchPathSet.Clear();
		ActiveMatchIndex = -1;

		if (string.IsNullOrEmpty(query))
		{
			return 0;
		}

		List<string> results = await StreamingJsonSearcher.SearchAsync(
			jsonBytes, query, options, progress, cancellationToken).ConfigureAwait(false);

		foreach (string path in results)
		{
			if (_matchPathSet.Add(path))
			{
				_matchPaths.Add(path);
			}
		}

		if (_matchPaths.Count > 0)
		{
			ActiveMatchIndex = 0;
		}

		return _matchPaths.Count;
	}

	#endregion

	#region Private Methods

	private void SearchPlainText(
		JsonElement element,
		string path,
		string query,
		StringComparison comparison,
		JsonSearchOptions options,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (JsonProperty prop in element.EnumerateObject())
				{
					string childPath = $"{path}/{JsonPointerHelper.EscapeSegment(prop.Name)}";

					if (options.SearchKeys && prop.Name.Contains(query, comparison))
					{
						_matchPaths.Add(childPath);
					}
					else
					{
						SearchPlainTextValue(prop.Value, childPath, query, comparison, options, cancellationToken);
					}

					SearchPlainText(prop.Value, childPath, query, comparison, options, cancellationToken);
				}

				break;

			case JsonValueKind.Array:
				int i = 0;
				foreach (JsonElement item in element.EnumerateArray())
				{
					string childPath = $"{path}/{i}";
					SearchPlainTextValue(item, childPath, query, comparison, options, cancellationToken);
					SearchPlainText(item, childPath, query, comparison, options, cancellationToken);
					i++;
				}

				break;
		}
	}

	private void SearchPlainTextValue(
		JsonElement element,
		string path,
		string query,
		StringComparison comparison,
		JsonSearchOptions options,
		CancellationToken cancellationToken)
	{
		if (!options.SearchValues)
		{
			return;
		}

		if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
		{
			return;
		}

		string valueText = element.ValueKind == JsonValueKind.String
			? element.GetString() ?? ""
			: element.GetRawText();

		if (valueText.Contains(query, comparison) && _matchPathSet.Add(path))
		{
			_matchPaths.Add(path);
		}
	}

	private void SearchWithRegex(
		JsonElement element,
		string path,
		Regex regex,
		JsonSearchOptions options,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (JsonProperty prop in element.EnumerateObject())
				{
					string childPath = $"{path}/{JsonPointerHelper.EscapeSegment(prop.Name)}";

					if (options.SearchKeys && regex.IsMatch(prop.Name))
					{
						_matchPaths.Add(childPath);
					}

					if (options.SearchValues &&
					    prop.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
					{
						string valueText = prop.Value.ValueKind == JsonValueKind.String
							? prop.Value.GetString() ?? ""
							: prop.Value.GetRawText();

						if (regex.IsMatch(valueText) && _matchPathSet.Add(childPath))
						{
							_matchPaths.Add(childPath);
						}
					}

					SearchWithRegex(prop.Value, childPath, regex, options, cancellationToken);
				}

				break;

			case JsonValueKind.Array:
				int i = 0;
				foreach (JsonElement item in element.EnumerateArray())
				{
					string childPath = $"{path}/{i}";

					if (options.SearchValues && item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
					{
						string valueText = item.ValueKind == JsonValueKind.String
							? item.GetString() ?? ""
							: item.GetRawText();

						if (regex.IsMatch(valueText))
						{
							_matchPaths.Add(childPath);
						}
					}

					SearchWithRegex(item, childPath, regex, options, cancellationToken);
					i++;
				}

				break;
		}
	}

	private void SearchPlainText(
		IJsonDocumentSource source,
		string path,
		string query,
		StringComparison comparison,
		JsonSearchOptions options,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		JsonValueKind kind = source.GetValueKind(path);
		if (kind is not (JsonValueKind.Object or JsonValueKind.Array))
		{
			return;
		}

		foreach (JsonChildDescriptor child in source.EnumerateChildren(path))
		{
			if (options.SearchKeys && child.PropertyName is not null &&
			    child.PropertyName.Contains(query, comparison))
			{
				_matchPaths.Add(child.Path);
				_matchPathSet.Add(child.Path);
			}

			if (options.SearchValues && child.RawValue is not null &&
			    child.RawValue.Contains(query, comparison) && _matchPathSet.Add(child.Path))
			{
				_matchPaths.Add(child.Path);
			}

			if (child.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				SearchPlainText(source, child.Path, query, comparison, options, cancellationToken);
			}
		}
	}

	private void SearchWithRegex(
		IJsonDocumentSource source,
		string path,
		Regex regex,
		JsonSearchOptions options,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		JsonValueKind kind = source.GetValueKind(path);
		if (kind is not (JsonValueKind.Object or JsonValueKind.Array))
		{
			return;
		}

		foreach (JsonChildDescriptor child in source.EnumerateChildren(path))
		{
			if (options.SearchKeys && child.PropertyName is not null && regex.IsMatch(child.PropertyName))
			{
				_matchPaths.Add(child.Path);
				_matchPathSet.Add(child.Path);
			}

			if (options.SearchValues && child.RawValue is not null &&
			    regex.IsMatch(child.RawValue) && _matchPathSet.Add(child.Path))
			{
				_matchPaths.Add(child.Path);
			}

			if (child.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				SearchWithRegex(source, child.Path, regex, options, cancellationToken);
			}
		}
	}

	#endregion
}
