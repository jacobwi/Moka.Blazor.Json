using System.Text.Json;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonTreeFlattenerTests
{
    [Fact]
    public void Flatten_SimpleObject_ReturnsCorrectNodes()
    {
        using var doc = JsonDocument.Parse("""{"a":1,"b":"hello"}""");
        var flattener = new JsonTreeFlattener();
        flattener.ExpandToDepth(doc.RootElement, 1);

        var nodes = flattener.Flatten(doc.RootElement);

        // Root opening + a + b + root closing = 4
        Assert.Equal(4, nodes.Count);
        Assert.Equal(JsonValueKind.Object, nodes[0].ValueKind);
        Assert.True(nodes[0].IsExpanded);
        Assert.Equal("a", nodes[1].PropertyName);
        Assert.Equal("b", nodes[2].PropertyName);
        Assert.True(nodes[3].IsClosingBracket);
    }

    [Fact]
    public void Flatten_CollapsedObject_ShowsSingleRow()
    {
        using var doc = JsonDocument.Parse("""{"a":1}""");
        var flattener = new JsonTreeFlattener();
        // Don't expand anything

        var nodes = flattener.Flatten(doc.RootElement);

        Assert.Single(nodes);
        Assert.False(nodes[0].IsExpanded);
        Assert.Equal(1, nodes[0].ChildCount);
    }

    [Fact]
    public void ToggleExpand_TogglesState()
    {
        var flattener = new JsonTreeFlattener();

        var expanded = flattener.ToggleExpand("/test");
        Assert.True(expanded);

        var collapsed = flattener.ToggleExpand("/test");
        Assert.False(collapsed);
    }

    [Fact]
    public void Flatten_Array_IncludesArrayIndices()
    {
        using var doc = JsonDocument.Parse("""[1,2,3]""");
        var flattener = new JsonTreeFlattener();
        flattener.ExpandToDepth(doc.RootElement, 1);

        var nodes = flattener.Flatten(doc.RootElement);

        // Root opening + 3 elements + root closing = 5
        Assert.Equal(5, nodes.Count);
        Assert.Equal(0, nodes[1].ArrayIndex);
        Assert.Equal(1, nodes[2].ArrayIndex);
        Assert.Equal(2, nodes[3].ArrayIndex);
    }

    [Fact]
    public void Flatten_TrailingCommas_SetCorrectly()
    {
        using var doc = JsonDocument.Parse("""{"a":1,"b":2,"c":3}""");
        var flattener = new JsonTreeFlattener();
        flattener.ExpandToDepth(doc.RootElement, 1);

        var nodes = flattener.Flatten(doc.RootElement);

        // a has comma, b has comma, c does not
        Assert.True(nodes[1].HasTrailingComma); // a
        Assert.True(nodes[2].HasTrailingComma); // b
        Assert.False(nodes[3].HasTrailingComma); // c
    }
}