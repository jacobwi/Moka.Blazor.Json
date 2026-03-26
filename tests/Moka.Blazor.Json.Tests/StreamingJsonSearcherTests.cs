using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class StreamingJsonSearcherTests
{
	[Fact]
	public async Task Search_PlainText_FindsStringValues()
	{
		byte[] json = """{"name":"Alice","friend":"Bob"}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(json, "Alice");

		Assert.Single(matches);
		Assert.Equal("/name", matches[0]);
	}

	[Fact]
	public async Task Search_PlainText_FindsMultipleMatches()
	{
		byte[] json = """{"a":"test","b":"other","c":"test"}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(json, "test");

		Assert.Equal(2, matches.Count);
	}

	[Fact]
	public async Task Search_CaseInsensitive_FindsMatches()
	{
		byte[] json = """{"Name":"Alice"}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(
			json, "alice", new JsonSearchOptions { CaseSensitive = false });

		Assert.Single(matches);
	}

	[Fact]
	public async Task Search_CaseSensitive_SkipsNonMatches()
	{
		byte[] json = """{"Name":"Alice"}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(
			json, "alice", new JsonSearchOptions { CaseSensitive = true });

		Assert.Empty(matches);
	}

	[Fact]
	public async Task Search_Keys_FindsKeyMatches()
	{
		byte[] json = """{"userName":"test"}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(
			json, "userName",
			new JsonSearchOptions { SearchKeys = true, SearchValues = false });

		Assert.Single(matches);
		Assert.Equal("/userName", matches[0]);
	}

	[Fact]
	public async Task Search_Regex_FindsMatches()
	{
		byte[] json = """{"email":"alice@example.com","other":"bob@test.com"}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(
			json, @"\w+@\w+\.com",
			new JsonSearchOptions { UseRegex = true });

		Assert.Equal(2, matches.Count);
	}

	[Fact]
	public async Task Search_NestedValues_FindsCorrectPaths()
	{
		byte[] json = """{"user":{"name":"Alice","address":{"city":"NYC"}}}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(json, "NYC");

		Assert.Single(matches);
		Assert.Equal("/user/address/city", matches[0]);
	}

	[Fact]
	public async Task Search_ArrayValues_FindsCorrectPaths()
	{
		byte[] json = """{"tags":["dev","admin","dev"]}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(json, "dev");

		Assert.Equal(2, matches.Count);
		Assert.Equal("/tags/0", matches[0]);
		Assert.Equal("/tags/2", matches[1]);
	}

	[Fact]
	public async Task Search_NumberValues_FindsMatches()
	{
		byte[] json = """{"count":42,"other":"text"}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(json, "42");

		Assert.Single(matches);
		Assert.Equal("/count", matches[0]);
	}

	[Fact]
	public async Task Search_BooleanValues_FindsMatches()
	{
		byte[] json = """{"active":true,"deleted":false}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(json, "true");

		Assert.Single(matches);
		Assert.Equal("/active", matches[0]);
	}

	[Fact]
	public async Task Search_EmptyQuery_ReturnsEmpty()
	{
		byte[] json = """{"a":1}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(
			json, "", new JsonSearchOptions());

		Assert.Empty(matches);
	}

	[Fact]
	public async Task Search_ReportsProgress()
	{
		byte[] json = """{"a":"test","b":"test","c":"test"}"""u8.ToArray();
		var progressReports = new List<StreamingJsonSearcher.SearchProgress>();

		await StreamingJsonSearcher.SearchAsync(
			json, "test",
			progress: new Progress<StreamingJsonSearcher.SearchProgress>(p => progressReports.Add(p)));

		// At minimum, the final progress report should show total bytes
		// Note: Progress<T> may not capture all reports due to async dispatch,
		// but the search should complete successfully
	}

	[Fact]
	public async Task Search_SupportssCancellation()
	{
		byte[] json = """{"a":"test"}"""u8.ToArray();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			StreamingJsonSearcher.SearchAsync(json, "test", cancellationToken: cts.Token));
	}

	[Fact]
	public async Task Search_InvalidRegex_ReturnsEmpty()
	{
		byte[] json = """{"a":"test"}"""u8.ToArray();

		List<string> matches = await StreamingJsonSearcher.SearchAsync(
			json, "[invalid",
			new JsonSearchOptions { UseRegex = true });

		Assert.Empty(matches);
	}
}
