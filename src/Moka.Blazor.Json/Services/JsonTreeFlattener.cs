using System.Text.Json;
using Moka.Blazor.Json.Models;
using Moka.Blazor.Json.Utilities;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Flattens a <see cref="JsonElement" /> tree into a list of <see cref="FlattenedJsonNode" />
///     for virtualized rendering. Only expands nodes that are marked as expanded.
/// </summary>
internal sealed class JsonTreeFlattener
{
	#region Fields

	// Flatten and ExpandAll run on background threads while UI event handlers mutate
	// expand/search state; HashSet is not safe for a concurrent reader and writer.
	private readonly Lock _gate = new();
	private readonly HashSet<string> _expandedPaths = new(StringComparer.Ordinal);
	private HashSet<string> _searchMatchPaths = new(StringComparer.Ordinal);
	private string? _activeSearchMatchPath;

	#endregion

	#region Properties

	/// <summary>
	///     Gets a snapshot of the currently expanded paths.
	/// </summary>
	public IReadOnlySet<string> ExpandedPaths
	{
		get
		{
			lock (_gate)
			{
				return new HashSet<string>(_expandedPaths, StringComparer.Ordinal);
			}
		}
	}

	#endregion

	#region Expand / Collapse

	/// <summary>
	///     Sets which paths are expanded based on depth.
	/// </summary>
	/// <param name="root">The root element.</param>
	/// <param name="maxDepth">Max depth to expand (-1 for all).</param>
	public void ExpandToDepth(JsonElement root, int maxDepth)
	{
		lock (_gate)
		{
			_expandedPaths.Clear();
			if (maxDepth != 0)
			{
				ExpandToDepthRecursive(root, "", 0, maxDepth);
			}
		}
	}

	/// <summary>
	///     Toggles the expanded state of a node at the given path.
	/// </summary>
	/// <param name="path">The JSON Pointer path.</param>
	/// <returns><c>true</c> if the node is now expanded; <c>false</c> if collapsed.</returns>
	public bool ToggleExpand(string path)
	{
		lock (_gate)
		{
			if (!_expandedPaths.Remove(path))
			{
				_expandedPaths.Add(path);
				return true;
			}

			return false;
		}
	}

	/// <summary>
	///     Expands a specific path.
	/// </summary>
	public void Expand(string path)
	{
		lock (_gate)
		{
			_expandedPaths.Add(path);
		}
	}

	/// <summary>
	///     Expands the root and every ancestor of <paramref name="path" /> so the node becomes visible.
	///     The node itself is left as it is.
	/// </summary>
	public void ExpandAncestors(string path)
	{
		lock (_gate)
		{
			_expandedPaths.Add("");

			int slash = path.Length > 1 ? path.IndexOf('/', 1) : -1;
			while (slash > 0)
			{
				_expandedPaths.Add(path[..slash]);
				slash = path.IndexOf('/', slash + 1);
			}
		}
	}

	/// <summary>
	///     Collapses a specific path.
	/// </summary>
	public void Collapse(string path)
	{
		lock (_gate)
		{
			_expandedPaths.Remove(path);
		}
	}

	/// <summary>
	///     Collapses a path and every expanded descendant of it.
	/// </summary>
	public void CollapseSubtree(string path)
	{
		string prefix = path + "/";
		lock (_gate)
		{
			_expandedPaths.RemoveWhere(p => p == path || p.StartsWith(prefix, StringComparison.Ordinal));
		}
	}

	/// <summary>
	///     Collapses all nodes.
	/// </summary>
	public void CollapseAll()
	{
		lock (_gate)
		{
			_expandedPaths.Clear();
		}
	}

	/// <summary>
	///     Replaces the expanded set, e.g. to keep the user's view after the document is re-parsed.
	/// </summary>
	public void RestoreExpandedPaths(IEnumerable<string> paths)
	{
		lock (_gate)
		{
			_expandedPaths.Clear();
			_expandedPaths.UnionWith(paths);
		}
	}

	/// <summary>
	///     Expands all nodes in the document.
	/// </summary>
	/// <param name="root">The root element.</param>
	public void ExpandAll(JsonElement root) => ExpandToDepth(root, -1);

	/// <summary>
	///     Sets which paths are expanded based on depth, using an <see cref="IJsonDocumentSource" />.
	/// </summary>
	public void ExpandToDepth(IJsonDocumentSource source, int maxDepth)
	{
		lock (_gate)
		{
			_expandedPaths.Clear();
			if (maxDepth != 0)
			{
				ExpandToDepthRecursive(source, "", source.RootValueKind, 0, maxDepth);
			}
		}
	}

	/// <summary>
	///     Expands all nodes using an <see cref="IJsonDocumentSource" />.
	/// </summary>
	public void ExpandAll(IJsonDocumentSource source) => ExpandToDepth(source, -1);

	/// <summary>
	///     Expands the container at <paramref name="path" /> and every container below it.
	/// </summary>
	public void ExpandSubtree(IJsonDocumentSource source, string path)
	{
		lock (_gate)
		{
			ExpandToDepthRecursive(source, path, source.GetValueKind(path), 0, -1);
		}
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
		var matches = new HashSet<string>(matchPaths, StringComparer.Ordinal);
		lock (_gate)
		{
			_searchMatchPaths = matches;
			_activeSearchMatchPath = activeMatchPath;
		}
	}

	/// <summary>
	///     Changes only the active match, keeping the existing match set.
	/// </summary>
	public void SetActiveSearchMatch(string? activeMatchPath)
	{
		lock (_gate)
		{
			_activeSearchMatchPath = activeMatchPath;
		}
	}

	/// <summary>
	///     Clears all search match highlighting.
	/// </summary>
	public void ClearSearchMatches()
	{
		lock (_gate)
		{
			_searchMatchPaths = new HashSet<string>(StringComparer.Ordinal);
			_activeSearchMatchPath = null;
		}
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
		int nextId = 0;
		var result = new List<FlattenedJsonNode>();
		lock (_gate)
		{
			FlattenElement(ref nextId, root, "", null, 0, false, result);
		}

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
		int nextId = 0;
		var result = new List<FlattenedJsonNode>();
		lock (_gate)
		{
			FlattenElement(ref nextId, scopedRoot, scopedPath, null, 0, false, result);
		}

		return result;
	}

	/// <summary>
	///     Flattens the document tree using an <see cref="IJsonDocumentSource" />.
	///     Only descends into expanded containers.
	/// </summary>
	public List<FlattenedJsonNode> Flatten(IJsonDocumentSource source)
	{
		int nextId = 0;
		var result = new List<FlattenedJsonNode>();
		lock (_gate)
		{
			FlattenFromSource(ref nextId, source, "", null, null, source.RootValueKind, null,
				source.GetChildCount(""), 0, false, result);
		}

		return result;
	}

	/// <summary>
	///     Flattens a subtree using an <see cref="IJsonDocumentSource" />, scoped to a specific path.
	/// </summary>
	public List<FlattenedJsonNode> FlattenScoped(IJsonDocumentSource source, string scopedPath)
	{
		int nextId = 0;
		var result = new List<FlattenedJsonNode>();
		JsonValueKind kind = source.GetValueKind(scopedPath);
		int childCount = source.GetChildCount(scopedPath);
		lock (_gate)
		{
			FlattenFromSource(ref nextId, source, scopedPath, null, null, kind, null, childCount, 0, false, result);
		}

		return result;
	}

	#endregion

	#region Private Methods

	private void FlattenElement(
		ref int nextId,
		JsonElement element,
		string path,
		string? propertyName,
		int depth,
		bool hasTrailingComma,
		List<FlattenedJsonNode> result)
	{
		int id = nextId++;
		bool isContainer = element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
		bool isExpanded = isContainer && _expandedPaths.Contains(path);
		int childCount = GetChildCount(element);

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

		if (!isContainer || !isExpanded)
		{
			return;
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			int i = 0;
			foreach (KeyValuePair<string, JsonElement> prop in JsonObjectReader.GetProperties(element))
			{
				string childPath = $"{path}/{JsonPointerHelper.EscapeSegment(prop.Key)}";
				bool isLast = i == childCount - 1;
				FlattenElement(ref nextId, prop.Value, childPath, prop.Key, depth + 1, !isLast, result);
				i++;
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			int i = 0;
			foreach (JsonElement item in element.EnumerateArray())
			{
				string childPath = $"{path}/{i}";
				bool isLast = i == childCount - 1;
				FlattenElement(ref nextId, item, childPath, null, depth + 1, !isLast, result);

				// Patch the array index on the last added node
				int lastIdx = result.Count - 1;
				FlattenedJsonNode lastNode = result[lastIdx];
				result[lastIdx] = lastNode with { ArrayIndex = i };
				i++;
			}
		}

		// Add closing bracket
		result.Add(new FlattenedJsonNode
		{
			Id = nextId++,
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

	private static int GetChildCount(JsonElement element) => JsonObjectReader.CountChildren(element, true);

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

	private void ExpandToDepthRecursive(JsonElement element, string path, int currentDepth, int maxDepth)
	{
		if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
		{
			return;
		}

		_expandedPaths.Add(path);

		if (maxDepth >= 0 && currentDepth >= maxDepth)
		{
			return;
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (KeyValuePair<string, JsonElement> prop in JsonObjectReader.GetProperties(element))
			{
				string childPath = $"{path}/{JsonPointerHelper.EscapeSegment(prop.Key)}";
				ExpandToDepthRecursive(prop.Value, childPath, currentDepth + 1, maxDepth);
			}
		}
		else
		{
			int i = 0;
			foreach (JsonElement item in element.EnumerateArray())
			{
				string childPath = $"{path}/{i}";
				ExpandToDepthRecursive(item, childPath, currentDepth + 1, maxDepth);
				i++;
			}
		}
	}

	private void ExpandToDepthRecursive(IJsonDocumentSource source, string path, JsonValueKind kind,
		int currentDepth, int maxDepth)
	{
		if (kind is not (JsonValueKind.Object or JsonValueKind.Array))
		{
			return;
		}

		_expandedPaths.Add(path);

		if (maxDepth >= 0 && currentDepth >= maxDepth)
		{
			return;
		}

		foreach (JsonChildDescriptor child in source.EnumerateChildren(path))
		{
			if (child.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			{
				ExpandToDepthRecursive(source, child.Path, child.ValueKind, currentDepth + 1, maxDepth);
			}
		}
	}

	private void FlattenFromSource(
		ref int nextId,
		IJsonDocumentSource source,
		string path,
		string? propertyName,
		int? arrayIndex,
		JsonValueKind valueKind,
		string? rawValue,
		int childCount,
		int depth,
		bool hasTrailingComma,
		List<FlattenedJsonNode> result)
	{
		int id = nextId++;
		bool isContainer = valueKind is JsonValueKind.Object or JsonValueKind.Array;
		bool isExpanded = isContainer && _expandedPaths.Contains(path);

		var node = new FlattenedJsonNode
		{
			Id = id,
			Path = path,
			Depth = depth,
			PropertyName = propertyName,
			ValueKind = valueKind,
			RawValue = isContainer ? null : rawValue,
			ChildCount = childCount,
			ArrayIndex = arrayIndex ?? -1,
			IsExpanded = isExpanded,
			IsClosingBracket = false,
			IsSearchMatch = _searchMatchPaths.Contains(path),
			IsActiveSearchMatch = _activeSearchMatchPath == path,
			HasTrailingComma = hasTrailingComma
		};

		result.Add(node);

		if (!isContainer || !isExpanded)
		{
			return;
		}

		int i = 0;
		foreach (JsonChildDescriptor child in source.EnumerateChildren(path))
		{
			bool isLast = i == childCount - 1;
			FlattenFromSource(ref nextId, source, child.Path, child.PropertyName, child.ArrayIndex,
				child.ValueKind, child.RawValue, child.ChildCount, depth + 1, !isLast, result);
			i++;
		}

		// Add closing bracket
		result.Add(new FlattenedJsonNode
		{
			Id = nextId++,
			Path = path,
			Depth = depth,
			PropertyName = null,
			ValueKind = valueKind,
			RawValue = null,
			ChildCount = 0,
			IsExpanded = false,
			IsClosingBracket = true,
			HasTrailingComma = hasTrailingComma
		});
	}

	#endregion
}
