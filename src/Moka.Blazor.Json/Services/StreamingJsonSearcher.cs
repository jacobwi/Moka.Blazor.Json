using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Searches raw UTF-8 JSON bytes using <see cref="Utf8JsonReader" /> without
///     building a full DOM, suitable for very large documents.
/// </summary>
internal static class StreamingJsonSearcher
{
	/// <summary>
	///     Searches JSON bytes for matches, yielding matching JSON Pointer paths.
	/// </summary>
	public static async Task<List<string>> SearchAsync(
		ReadOnlyMemory<byte> jsonBytes,
		string query,
		JsonSearchOptions? options = null,
		IProgress<SearchProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(query))
		{
			return [];
		}

		return await Task.Run(
			() => Search(jsonBytes.Span, query, options ?? new JsonSearchOptions(), progress,
				jsonBytes.Length, cancellationToken),
			cancellationToken).ConfigureAwait(false);
	}

	private static List<string> Search(
		ReadOnlySpan<byte> jsonBytes,
		string query,
		JsonSearchOptions options,
		IProgress<SearchProgress>? progress,
		long totalBytes,
		CancellationToken cancellationToken)
	{
		var matches = new List<string>();
		var reader = new Utf8JsonReader(jsonBytes, new JsonReaderOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
			MaxDepth = 256
		});

		// Path tracking
		var pathStack = new Stack<(string Segment, JsonValueKind ParentKind, int ChildIndex)>();
		string? currentPropertyName = null;
		int tokensProcessed = 0;

		Regex? regex = null;
		if (options.UseRegex)
		{
			RegexOptions regexOpts = RegexOptions.Compiled;
			if (!options.CaseSensitive)
			{
				regexOpts |= RegexOptions.IgnoreCase;
			}

			try
			{
				regex = new Regex(query, regexOpts, TimeSpan.FromSeconds(1));
			}
			catch (RegexParseException)
			{
				return matches;
			}
		}

		StringComparison comparison = options.CaseSensitive
			? StringComparison.Ordinal
			: StringComparison.OrdinalIgnoreCase;

		while (reader.Read())
		{
			if (++tokensProcessed % 5000 == 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				progress?.Report(new SearchProgress(matches.Count, reader.BytesConsumed, totalBytes));
			}

			switch (reader.TokenType)
			{
				case JsonTokenType.StartObject:
				{
					string path = BuildCurrentPath(pathStack, currentPropertyName);
					pathStack.Push((path, JsonValueKind.Object, 0));
					currentPropertyName = null;
					break;
				}

				case JsonTokenType.StartArray:
				{
					string path = BuildCurrentPath(pathStack, currentPropertyName);
					pathStack.Push((path, JsonValueKind.Array, 0));
					currentPropertyName = null;
					break;
				}

				case JsonTokenType.EndObject:
				case JsonTokenType.EndArray:
				{
					if (pathStack.Count > 0)
					{
						pathStack.Pop();
					}

					// Increment parent's child index if parent is an array
					IncrementParentArrayIndex(pathStack);
					break;
				}

				case JsonTokenType.PropertyName:
				{
					currentPropertyName = reader.GetString();

					// Check key match
					if (options.SearchKeys && currentPropertyName is not null)
					{
						string keyPath = BuildCurrentPath(pathStack, currentPropertyName);
						bool isMatch = regex is not null
							? regex.IsMatch(currentPropertyName)
							: currentPropertyName.Contains(query, comparison);

						if (isMatch)
						{
							matches.Add(keyPath);
						}
					}

					break;
				}

				case JsonTokenType.String:
				{
					if (options.SearchValues)
					{
						string? value = reader.GetString();
						if (value is not null)
						{
							bool isMatch = regex is not null
								? regex.IsMatch(value)
								: value.Contains(query, comparison);

							if (isMatch)
							{
								string path = BuildCurrentPath(pathStack, currentPropertyName);
								matches.Add(path);
							}
						}
					}

					currentPropertyName = null;
					IncrementParentArrayIndex(pathStack);
					break;
				}

				case JsonTokenType.Number:
				case JsonTokenType.True:
				case JsonTokenType.False:
				case JsonTokenType.Null:
				{
					if (options.SearchValues)
					{
						string valueText = reader.TokenType switch
						{
							JsonTokenType.True => "true",
							JsonTokenType.False => "false",
							JsonTokenType.Null => "null",
							_ => Encoding.UTF8.GetString(
								jsonBytes.Slice((int)reader.TokenStartIndex,
									(int)(reader.BytesConsumed - reader.TokenStartIndex)))
						};

						bool isMatch = regex is not null
							? regex.IsMatch(valueText)
							: valueText.Contains(query, comparison);

						if (isMatch)
						{
							string path = BuildCurrentPath(pathStack, currentPropertyName);
							matches.Add(path);
						}
					}

					currentPropertyName = null;
					IncrementParentArrayIndex(pathStack);
					break;
				}
			}
		}

		progress?.Report(new SearchProgress(matches.Count, totalBytes, totalBytes));
		return matches;
	}

	private static string BuildCurrentPath(
		Stack<(string Segment, JsonValueKind ParentKind, int ChildIndex)> pathStack,
		string? propertyName)
	{
		if (pathStack.Count == 0)
		{
			return "";
		}

		(string parentPath, JsonValueKind parentKind, int childIndex) = pathStack.Peek();

		if (parentKind == JsonValueKind.Object && propertyName is not null)
		{
			return $"{parentPath}/{JsonPointerHelper.EscapeSegment(propertyName)}";
		}

		if (parentKind == JsonValueKind.Array)
		{
			return $"{parentPath}/{childIndex}";
		}

		return parentPath;
	}

	private static void IncrementParentArrayIndex(
		Stack<(string Segment, JsonValueKind ParentKind, int ChildIndex)> pathStack)
	{
		if (pathStack.Count > 0)
		{
			(string seg, JsonValueKind kind, int idx) = pathStack.Peek();
			if (kind == JsonValueKind.Array)
			{
				pathStack.Pop();
				pathStack.Push((seg, kind, idx + 1));
			}
		}
	}

	/// <summary>
	///     Progress data reported during a streaming search.
	/// </summary>
	internal readonly record struct SearchProgress(int MatchCount, long BytesScanned, long TotalBytes);
}
