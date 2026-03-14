using System.Text.Json;
using Moka.Blazor.Json.Abstractions;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonSearchEngineTests
{
    private readonly JsonSearchEngine _engine = new();

    [Fact]
    public void Search_PlainText_FindsMatches()
    {
        using var doc = JsonDocument.Parse("""{"name":"Alice","friend":"Bob"}""");

        var count = _engine.Search(doc.RootElement, "Alice");

        Assert.Equal(1, count);
        Assert.Equal("/name", _engine.MatchPaths[0]);
    }

    [Fact]
    public void Search_CaseInsensitive_FindsMatches()
    {
        using var doc = JsonDocument.Parse("""{"Name":"Alice"}""");

        var count = _engine.Search(doc.RootElement, "alice", new JsonSearchOptions { CaseSensitive = false });

        Assert.Equal(1, count);
    }

    [Fact]
    public void Search_CaseSensitive_SkipsNonMatches()
    {
        using var doc = JsonDocument.Parse("""{"Name":"Alice"}""");

        var count = _engine.Search(doc.RootElement, "alice", new JsonSearchOptions { CaseSensitive = true });

        Assert.Equal(0, count);
    }

    [Fact]
    public void Search_Keys_FindsKeyMatches()
    {
        using var doc = JsonDocument.Parse("""{"userName":"test"}""");

        var count = _engine.Search(doc.RootElement, "userName",
            new JsonSearchOptions { SearchKeys = true, SearchValues = false });

        Assert.Equal(1, count);
    }

    [Fact]
    public void Search_Regex_FindsMatches()
    {
        using var doc = JsonDocument.Parse("""{"email":"alice@example.com","other":"bob@test.com"}""");

        var count = _engine.Search(doc.RootElement, @"\w+@\w+\.com", new JsonSearchOptions { UseRegex = true });

        Assert.Equal(2, count);
    }

    [Fact]
    public void NextMatch_CyclesThrough()
    {
        using var doc = JsonDocument.Parse("""{"a":"x","b":"x","c":"x"}""");
        _engine.Search(doc.RootElement, "x");

        Assert.Equal(0, _engine.ActiveMatchIndex);
        _engine.NextMatch();
        Assert.Equal(1, _engine.ActiveMatchIndex);
        _engine.NextMatch();
        Assert.Equal(2, _engine.ActiveMatchIndex);
        _engine.NextMatch();
        Assert.Equal(0, _engine.ActiveMatchIndex); // wraps around
    }

    [Fact]
    public void Clear_ResetsState()
    {
        using var doc = JsonDocument.Parse("""{"a":"test"}""");
        _engine.Search(doc.RootElement, "test");
        Assert.Equal(1, _engine.MatchCount);

        _engine.Clear();

        Assert.Equal(0, _engine.MatchCount);
        Assert.Equal(-1, _engine.ActiveMatchIndex);
    }
}