using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

internal sealed record JsonSearchResult(IReadOnlyList<string> Paths, bool IsTruncated);

// A path is only built for a match, so the scan allocates almost nothing however large the document is.
internal static class StreamingJsonSearcher
{
	private const int MaxCachedNames = 100_000;

	public static async Task<List<string>> SearchAsync(
		ReadOnlyMemory<byte> jsonBytes,
		string query,
		JsonSearchOptions? options = null,
		IProgress<SearchProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		using var bytes = new MemoryJsonByteSource(jsonBytes);
		JsonSearchResult result = await SearchAsync(bytes, 0, jsonBytes.Length, "", query, options, int.MaxValue,
			progress, cancellationToken).ConfigureAwait(false);
		return [..result.Paths];
	}

	public static Task<JsonSearchResult> SearchAsync(
		IJsonByteSource bytes,
		long start,
		long end,
		string rootPath,
		string query,
		JsonSearchOptions? options,
		int maxMatches,
		IProgress<SearchProgress>? progress,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(query))
		{
			return Task.FromResult(new JsonSearchResult([], false));
		}

		return Task.Run(async () =>
		{
			try
			{
				var scanner = new Scanner(rootPath, query, options ?? new JsonSearchOptions(), maxMatches);
				using var feeder = new JsonBlockFeeder(bytes, start, end, JsonIndexLimits.Default.BlockBytes);

				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();

					int consumed = scanner.ProcessBlock(feeder.Window.Span, feeder.IsFinal);
					if (scanner.IsFull)
					{
						return new JsonSearchResult(scanner.Matches, true);
					}

					if (feeder.IsFinal)
					{
						progress?.Report(new SearchProgress(scanner.Matches.Count, end - start, end - start));
						return new JsonSearchResult(scanner.Matches, false);
					}

					feeder.Advance(consumed);
					progress?.Report(new SearchProgress(scanner.Matches.Count, feeder.WindowOffset - start, end - start));
					await BrowserThread.YieldAsync().ConfigureAwait(false);
				}
			}
			catch (RegexMatchTimeoutException)
			{
				return new JsonSearchResult([], false);
			}
			catch (ArgumentException)
			{
				return new JsonSearchResult([], false);
			}
		}, cancellationToken);
	}

	internal readonly record struct SearchProgress(int MatchCount, long BytesScanned, long TotalBytes);

	private sealed class Scanner
	{
		private readonly StringBuilder _path = new();
		private readonly List<OpenContainer> _open = [];
		private readonly Dictionary<ulong, bool> _nameResults = [];
		private readonly string _rootPath;
		private readonly TextMatcher _matcher;
		private readonly JsonSearchOptions _options;
		private readonly int _maxMatches;
		private JsonReaderState _state = new(JsonFormatting.LenientReader);
		private string? _lastMatch;
		private int _depth = -1;

		public Scanner(string rootPath, string query, JsonSearchOptions options, int maxMatches)
		{
			_rootPath = rootPath;
			_options = options;
			_maxMatches = maxMatches;
			_matcher = new TextMatcher(query, options);
		}

		public List<string> Matches { get; } = [];

		public bool IsFull => Matches.Count >= _maxMatches;

		public int ProcessBlock(ReadOnlySpan<byte> window, bool isFinal)
		{
			var reader = new Utf8JsonReader(window, isFinal, _state);

			while (!IsFull && reader.Read())
			{
				switch (reader.TokenType)
				{
					case JsonTokenType.StartObject:
						Open(false);
						break;

					case JsonTokenType.StartArray:
						Open(true);
						break;

					case JsonTokenType.EndObject:
					case JsonTokenType.EndArray:
						_depth--;
						AfterValue();
						break;

					case JsonTokenType.PropertyName:
						_open[_depth].SetName(reader.ValueSpan, reader.ValueIsEscaped);
						if (_options.SearchKeys && NameMatches(ref reader))
						{
							AddMatch();
						}

						break;

					case JsonTokenType.String:
						if (_options.SearchValues && _matcher.StringMatches(ref reader))
						{
							AddMatch();
						}

						AfterValue();
						break;

					case JsonTokenType.Number:
						if (_options.SearchValues && _matcher.NumberMatches(reader.ValueSpan))
						{
							AddMatch();
						}

						AfterValue();
						break;

					case JsonTokenType.True:
						if (_options.SearchValues && _matcher.MatchesTrue)
						{
							AddMatch();
						}

						AfterValue();
						break;

					case JsonTokenType.False:
						if (_options.SearchValues && _matcher.MatchesFalse)
						{
							AddMatch();
						}

						AfterValue();
						break;

					case JsonTokenType.Null:
						if (_options.SearchValues && _matcher.MatchesNull)
						{
							AddMatch();
						}

						AfterValue();
						break;
				}
			}

			_state = reader.CurrentState;
			return (int)reader.BytesConsumed;
		}

		private void Open(bool isArray)
		{
			_depth++;
			if (_depth == _open.Count)
			{
				_open.Add(new OpenContainer());
			}

			_open[_depth].Reset(isArray);
		}

		private void AfterValue()
		{
			if (_depth >= 0)
			{
				_open[_depth].Index++;
			}
		}

		// Documents repeat a small set of property names millions of times, so each is only tested once.
		private bool NameMatches(ref Utf8JsonReader reader)
		{
			if (reader.ValueIsEscaped)
			{
				return _matcher.StringMatches(ref reader);
			}

			ulong hash = JsonObjectReader.HashBytes(reader.ValueSpan);
			if (_nameResults.TryGetValue(hash, out bool known))
			{
				return known;
			}

			bool matches = _matcher.StringMatches(ref reader);
			if (_nameResults.Count < MaxCachedNames)
			{
				_nameResults.Add(hash, matches);
			}

			return matches;
		}

		private void AddMatch()
		{
			_path.Clear().Append(_rootPath);
			for (int i = 0; i <= _depth; i++)
			{
				_path.Append('/');
				_open[i].AppendSegment(_path);
			}

			string path = _path.ToString();

			// A key and its value can both match; they are one row.
			if (path != _lastMatch)
			{
				Matches.Add(path);
				_lastMatch = path;
			}
		}
	}

	private sealed class OpenContainer
	{
		private bool _isArray;
		private byte[] _name = new byte[64];
		private int _nameLength;
		private bool _nameIsEscaped;

		public int Index { get; set; }

		public void Reset(bool isArray)
		{
			_isArray = isArray;
			Index = 0;
			_nameLength = 0;
		}

		public void SetName(ReadOnlySpan<byte> name, bool isEscaped)
		{
			if (name.Length > _name.Length)
			{
				_name = new byte[name.Length * 2];
			}

			name.CopyTo(_name);
			_nameLength = name.Length;
			_nameIsEscaped = isEscaped;
		}

		public void AppendSegment(StringBuilder path)
		{
			if (_isArray)
			{
				path.Append(CultureInfo.InvariantCulture, $"{Index}");
				return;
			}

			path.Append(JsonPointerHelper.EscapeSegment(DecodeName()));
		}

		private string DecodeName()
		{
			ReadOnlySpan<byte> name = _name.AsSpan(0, _nameLength);
			if (!_nameIsEscaped)
			{
				return Encoding.UTF8.GetString(name);
			}

			byte[] quoted = new byte[name.Length + 2];
			quoted[0] = (byte)'"';
			name.CopyTo(quoted.AsSpan(1));
			quoted[^1] = (byte)'"';

			var reader = new Utf8JsonReader(quoted);
			reader.Read();
			return reader.GetString() ?? "";
		}
	}

	private sealed class TextMatcher
	{
		private static readonly SearchValues<char> NumberCharacters = SearchValues.Create("0123456789+-.eE");

		private readonly StringComparison _comparison;
		private readonly bool _numbersCanMatch;
		private readonly string _query;
		private readonly Regex? _regex;
		private char[] _chars = new char[1024];

		public TextMatcher(string query, JsonSearchOptions options)
		{
			_query = query;
			_comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

			if (options.UseRegex)
			{
				RegexOptions regexOptions = RegexOptions.CultureInvariant;
				if (!options.CaseSensitive)
				{
					regexOptions |= RegexOptions.IgnoreCase;
				}

				_regex = new Regex(query, regexOptions, TimeSpan.FromSeconds(1));
			}

			_numbersCanMatch = _regex is not null || query.AsSpan().IndexOfAnyExcept(NumberCharacters) < 0;

			MatchesTrue = Test("true");
			MatchesFalse = Test("false");
			MatchesNull = Test("null");
		}

		public bool MatchesTrue { get; }

		public bool MatchesFalse { get; }

		public bool MatchesNull { get; }

		public bool NumberMatches(ReadOnlySpan<byte> number) => _numbersCanMatch && Test(Decode(number));

		public bool StringMatches(ref Utf8JsonReader reader)
		{
			if (!reader.ValueIsEscaped)
			{
				return Test(Decode(reader.ValueSpan));
			}

			EnsureCapacity(reader.ValueSpan.Length);
			try
			{
				return Test(_chars.AsSpan(0, reader.CopyString(_chars)));
			}
			catch (InvalidOperationException)
			{
				// An escape that isn't valid UTF-16, such as a lone surrogate, has no text to match.
				return false;
			}
		}

		private bool Test(ReadOnlySpan<char> text) =>
			_regex?.IsMatch(text) ?? text.Contains(_query, _comparison);

		private ReadOnlySpan<char> Decode(ReadOnlySpan<byte> utf8)
		{
			EnsureCapacity(utf8.Length);
			return _chars.AsSpan(0, Encoding.UTF8.GetChars(utf8, _chars));
		}

		private void EnsureCapacity(int length)
		{
			if (length > _chars.Length)
			{
				_chars = new char[length * 2];
			}
		}
	}
}
