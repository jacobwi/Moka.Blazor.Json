using System.Text.Json;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Flattens a <see cref="JsonElement" /> tree into a list of <see cref="FlattenedJsonNode" />
///     for virtualized rendering. Only expands nodes that are marked as expanded.
/// </summary>
internal sealed class JsonTreeFlattener
{
    #region Properties

    /// <summary>
    ///     Gets the set of currently expanded paths, for external inspection.
    /// </summary>
    public IReadOnlySet<string> ExpandedPaths => _expandedPaths;

    #endregion

    #region Fields

    private readonly HashSet<string> _expandedPaths = new();
    private readonly HashSet<string> _searchMatchPaths = new();
    private string? _activeSearchMatchPath;
    private int _nextId;

    #endregion

    #region Expand / Collapse

    /// <summary>
    ///     Sets which paths are expanded based on depth.
    /// </summary>
    /// <param name="root">The root element.</param>
    /// <param name="maxDepth">Max depth to expand (-1 for all).</param>
    public void ExpandToDepth(JsonElement root, int maxDepth)
    {
        _expandedPaths.Clear();
        if (maxDepth != 0) ExpandToDepthRecursive(root, "", 0, maxDepth);
    }

    /// <summary>
    ///     Toggles the expanded state of a node at the given path.
    /// </summary>
    /// <param name="path">The JSON Pointer path.</param>
    /// <returns><c>true</c> if the node is now expanded; <c>false</c> if collapsed.</returns>
    public bool ToggleExpand(string path)
    {
        if (!_expandedPaths.Remove(path))
        {
            _expandedPaths.Add(path);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Expands a specific path.
    /// </summary>
    public void Expand(string path)
    {
        _expandedPaths.Add(path);
    }

    /// <summary>
    ///     Collapses a specific path.
    /// </summary>
    public void Collapse(string path)
    {
        _expandedPaths.Remove(path);
    }

    /// <summary>
    ///     Collapses all nodes.
    /// </summary>
    public void CollapseAll()
    {
        _expandedPaths.Clear();
    }

    /// <summary>
    ///     Expands all nodes in the document.
    /// </summary>
    /// <param name="root">The root element.</param>
    public void ExpandAll(JsonElement root)
    {
        ExpandToDepth(root, -1);
    }

    #endregion

    #region Search Matches

    /// <summary>
    ///     Sets the search match paths for highlighting.
    /// </summary>
    /// <param name="matchPaths">Paths that match the search query.</param>
    /// <param name="activeMatchPath">The currently active (focused) match path.</param>
    public void SetSearchMatches(IEnumerable<string> matchPaths, string? activeMatchPath)
    {
        _searchMatchPaths.Clear();
        foreach (var path in matchPaths) _searchMatchPaths.Add(path);
        _activeSearchMatchPath = activeMatchPath;
    }

    /// <summary>
    ///     Clears all search match highlighting.
    /// </summary>
    public void ClearSearchMatches()
    {
        _searchMatchPaths.Clear();
        _activeSearchMatchPath = null;
    }

    #endregion

    #region Flatten

    /// <summary>
    ///     Flattens the element tree into a list of nodes for rendering.
    ///     Only descends into expanded containers.
    /// </summary>
    /// <param name="root">The root JSON element.</param>
    /// <returns>A list of flattened nodes representing the visible tree.</returns>
    public List<FlattenedJsonNode> Flatten(JsonElement root)
    {
        _nextId = 0;
        var result = new List<FlattenedJsonNode>();
        FlattenElement(root, "", null, 0, false, result);
        return result;
    }

    /// <summary>
    ///     Flattens a subtree starting from a scoped element.
    ///     The scoped element becomes the new root at depth 0.
    /// </summary>
    /// <param name="scopedRoot">The element to use as the new root.</param>
    /// <param name="scopedPath">The JSON Pointer path to the scoped element (used for path tracking).</param>
    /// <returns>A list of flattened nodes representing the visible subtree.</returns>
    public List<FlattenedJsonNode> FlattenScoped(JsonElement scopedRoot, string scopedPath)
    {
        _nextId = 0;
        var result = new List<FlattenedJsonNode>();
        FlattenElement(scopedRoot, scopedPath, null, 0, false, result);
        return result;
    }

    #endregion

    #region Private Methods

    private void FlattenElement(
        JsonElement element,
        string path,
        string? propertyName,
        int depth,
        bool hasTrailingComma,
        List<FlattenedJsonNode> result)
    {
        var id = _nextId++;
        var isContainer = element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        var isExpanded = isContainer && _expandedPaths.Contains(path);
        var childCount = GetChildCount(element);

        var node = new FlattenedJsonNode
        {
            Id = id,
            Path = path,
            Depth = depth,
            PropertyName = propertyName,
            ValueKind = element.ValueKind,
            RawValue = isContainer ? null : GetRawValue(element),
            ChildCount = childCount,
            ArrayIndex = -1,
            IsExpanded = isExpanded,
            IsClosingBracket = false,
            IsSearchMatch = _searchMatchPaths.Contains(path),
            IsActiveSearchMatch = _activeSearchMatchPath == path,
            HasTrailingComma = hasTrailingComma
        };

        result.Add(node);

        if (!isContainer || !isExpanded) return;

        if (element.ValueKind == JsonValueKind.Object)
        {
            var i = 0;
            foreach (var prop in element.EnumerateObject())
            {
                var childPath = $"{path}/{EscapeJsonPointer(prop.Name)}";
                var isLast = i == childCount - 1;
                FlattenElement(prop.Value, childPath, prop.Name, depth + 1, !isLast, result);
                i++;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in element.EnumerateArray())
            {
                var childPath = $"{path}/{i}";
                var isLast = i == childCount - 1;
                FlattenElement(item, childPath, null, depth + 1, !isLast, result);

                // Patch the array index on the last added node
                var lastIdx = result.Count - 1;
                var lastNode = result[lastIdx];
                result[lastIdx] = lastNode with { ArrayIndex = i };
                i++;
            }
        }

        // Add closing bracket
        result.Add(new FlattenedJsonNode
        {
            Id = _nextId++,
            Path = path,
            Depth = depth,
            PropertyName = null,
            ValueKind = element.ValueKind,
            RawValue = null,
            ChildCount = 0,
            IsExpanded = false,
            IsClosingBracket = true,
            HasTrailingComma = hasTrailingComma
        });
    }

    private static int GetChildCount(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var count = 0;
            foreach (var _ in element.EnumerateObject()) count++;
            return count;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Array => element.GetArrayLength(),
            _ => 0
        };
    }

    private static string? GetRawValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => null
        };
    }

    private static string EscapeJsonPointer(string segment)
    {
        return segment.Replace("~", "~0").Replace("/", "~1");
    }

    private void ExpandToDepthRecursive(JsonElement element, string path, int currentDepth, int maxDepth)
    {
        if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return;

        _expandedPaths.Add(path);

        if (maxDepth >= 0 && currentDepth >= maxDepth) return;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var childPath = $"{path}/{EscapeJsonPointer(prop.Name)}";
                ExpandToDepthRecursive(prop.Value, childPath, currentDepth + 1, maxDepth);
            }
        }
        else
        {
            var i = 0;
            foreach (var item in element.EnumerateArray())
            {
                var childPath = $"{path}/{i}";
                ExpandToDepthRecursive(item, childPath, currentDepth + 1, maxDepth);
                i++;
            }
        }
    }

    #endregion
}