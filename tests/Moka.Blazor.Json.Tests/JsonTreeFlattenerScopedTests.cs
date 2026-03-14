using System.Text.Json;
using Moka.Blazor.Json.Services;
using Xunit;

namespace Moka.Blazor.Json.Tests;

public sealed class JsonTreeFlattenerScopedTests
{
    [Fact]
    public void FlattenScoped_ShowsOnlyScopedSubtree()
    {
        using var doc = JsonDocument.Parse("""{"users":[{"name":"Alice"},{"name":"Bob"}],"count":2}""");
        var flattener = new JsonTreeFlattener();
        flattener.ExpandToDepth(doc.RootElement, 3);

        // Scope to /users
        var usersElement = doc.RootElement.GetProperty("users");
        var nodes = flattener.FlattenScoped(usersElement, "/users");

        // Should start with the array node at /users
        Assert.Equal(JsonValueKind.Array, nodes[0].ValueKind);
        Assert.Equal("/users", nodes[0].Path);
        Assert.Equal(0, nodes[0].Depth); // Scoped root is depth 0
    }

    [Fact]
    public void FlattenScoped_ChildrenUseScopedRootPaths()
    {
        using var doc = JsonDocument.Parse("""{"config":{"a":1,"b":2}}""");
        var flattener = new JsonTreeFlattener();
        flattener.ExpandToDepth(doc.RootElement, 3);

        var configElement = doc.RootElement.GetProperty("config");
        var nodes = flattener.FlattenScoped(configElement, "/config");

        // Children should have paths relative to the scoped path
        var childPaths = nodes.Where(n => !n.IsClosingBracket).Select(n => n.Path).ToList();
        Assert.Contains("/config", childPaths);
        Assert.Contains("/config/a", childPaths);
        Assert.Contains("/config/b", childPaths);
    }
}