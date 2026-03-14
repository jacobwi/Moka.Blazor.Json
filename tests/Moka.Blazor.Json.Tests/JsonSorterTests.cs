using System.Text.Json;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonSorterTests
{
    [Fact]
    public void SortKeys_SortsTopLevelKeys()
    {
        using var doc = JsonDocument.Parse("""{"c":3,"a":1,"b":2}""");

        var sorted = JsonSorter.SortKeys(doc.RootElement, false);

        using var result = JsonDocument.Parse(sorted);
        var keys = result.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["a", "b", "c"], keys);
    }

    [Fact]
    public void SortKeys_DoesNotSortNestedKeys()
    {
        using var doc = JsonDocument.Parse("""{"b":{"z":1,"a":2},"a":1}""");

        var sorted = JsonSorter.SortKeys(doc.RootElement, false);

        using var result = JsonDocument.Parse(sorted);
        var topKeys = result.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["a", "b"], topKeys);

        // Nested keys should NOT be sorted
        var nestedKeys = result.RootElement.GetProperty("b").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["z", "a"], nestedKeys);
    }

    [Fact]
    public void SortKeysRecursive_SortsAllLevels()
    {
        using var doc = JsonDocument.Parse("""{"b":{"z":1,"a":2},"a":1}""");

        var sorted = JsonSorter.SortKeysRecursive(doc.RootElement, false);

        using var result = JsonDocument.Parse(sorted);
        var topKeys = result.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["a", "b"], topKeys);

        var nestedKeys = result.RootElement.GetProperty("b").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["a", "z"], nestedKeys);
    }

    [Fact]
    public void SortKeysRecursive_SortsObjectsInsideArrays()
    {
        using var doc = JsonDocument.Parse("""[{"c":1,"a":2},{"z":3,"b":4}]""");

        var sorted = JsonSorter.SortKeysRecursive(doc.RootElement, false);

        using var result = JsonDocument.Parse(sorted);
        var firstKeys = result.RootElement[0].EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["a", "c"], firstKeys);

        var secondKeys = result.RootElement[1].EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["b", "z"], secondKeys);
    }

    [Fact]
    public void SortKeys_PreservesValues()
    {
        using var doc = JsonDocument.Parse("""{"b":"hello","a":42,"c":true}""");

        var sorted = JsonSorter.SortKeys(doc.RootElement, false);

        using var result = JsonDocument.Parse(sorted);
        Assert.Equal(42, result.RootElement.GetProperty("a").GetInt32());
        Assert.Equal("hello", result.RootElement.GetProperty("b").GetString());
        Assert.True(result.RootElement.GetProperty("c").GetBoolean());
    }

    [Fact]
    public void SortKeys_NonObject_ReturnsAsIs()
    {
        using var doc = JsonDocument.Parse("[1,2,3]");

        var sorted = JsonSorter.SortKeys(doc.RootElement, false);

        Assert.Equal("[1,2,3]", sorted);
    }
}