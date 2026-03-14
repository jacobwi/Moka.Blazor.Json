using System.Text.Json;
using System.Text.RegularExpressions;
using Moka.Blazor.Json.Abstractions;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Provides search functionality across a JSON document, supporting
///     plain text, regex, and JSON Path queries.
/// </summary>
internal sealed class JsonSearchEngine
{
	#region Fields

	private readonly List<string> _matchPaths = new();
	private readonly HashSet<string> _matchPathSet = new();

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
					string childPath = $"{path}/{EscapeJsonPointer(prop.Name)}";

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
					string childPath = $"{path}/{EscapeJsonPointer(prop.Name)}";

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

	private static string EscapeJsonPointer(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

	#endregion
}
