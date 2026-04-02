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
	#region Properties

	/// <summary>
	///     Gets the set of currently expanded paths, for external inspection.
	/// </summary>
	public IReadOnlySet<string> ExpandedPaths => _expandedPaths;

	#endregion

	#region Fields

	private readonly HashSet<string> _expandedPaths = [];
	private readonly HashSet<string> _searchMatchPaths = [];
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
		if (maxDepth != 0)
		{
			ExpandToDepthRecursive(root, "", 0, maxDepth);
		}
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
	public void Expand(string path) => _expandedPaths.Add(path);

	/// <summary>
	///     Collapses a specific path.
	/// </summary>
	public void Collapse(string path) => _expandedPaths.Remove(path);

	/// <summary>
	///     Collapses all nodes.
	/// </summary>
	public void CollapseAll() => _expandedPaths.Clear();

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
		_expandedPaths.Clear();
		if (maxDepth != 0)
		{
			ExpandToDepthRecursive(source, "", 0, maxDepth);
		}
	}

	/// <summary>
	///     Expands all nodes using an <see cref="IJsonDocumentSource" />.
	/// </summary>
	public void ExpandAll(IJsonDocumentSource source) => ExpandToDepth(source, -1);

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
		foreach (string path in matchPaths)
		{
			_searchMatchPaths.Add(path);
		}

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

	/// <summary>
	///     Flattens the document tree using an <see cref="IJsonDocumentSource" />.
	///     Only descends into expanded containers.
	/// </summary>
	public List<FlattenedJsonNode> Flatten(IJsonDocumentSource source)
	{
		_nextId = 0;
		var result = new List<FlattenedJsonNode>();
		FlattenFromSource(source, "", null, null, source.RootValueKind, null,
			source.GetChildCount(""), 0, false, result);
		return result;
	}

	/// <summary>
	///     Flattens a subtree using an <see cref="IJsonDocumentSource" />, scoped to a specific path.
	/// </summary>
	public List<FlattenedJsonNode> FlattenScoped(IJsonDocumentSource source, string scopedPath)
	{
		_nextId = 0;
		var result = new List<FlattenedJsonNode>();
		JsonValueKind kind = source.GetValueKind(scopedPath);
		int childCount = source.GetChildCount(scopedPath);
		FlattenFromSource(source, scopedPath, null, null, kind, null, childCount, 0, false, result);
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
		int id = _nextId++;
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
			foreach (JsonProperty prop in element.EnumerateObject())
			{
				string childPath = $"{path}/{JsonPointerHelper.EscapeSegment(prop.Name)}";
				bool isLast = i == childCount - 1;
				FlattenElement(prop.Value, childPath, prop.Name, depth + 1, !isLast, result);
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
				FlattenElement(item, childPath, null, depth + 1, !isLast, result);

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
			int count = 0;
			foreach (JsonProperty _ in element.EnumerateObject())
			{
				count++;
			}

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
			foreach (JsonProperty prop in element.EnumerateObject())
			{
				string childPath = $"{path}/{JsonPointerHelper.EscapeSegment(prop.Name)}";
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

	private void ExpandToDepthRecursive(IJsonDocumentSource source, string path, int currentDepth, int maxDepth)
	{
		JsonValueKind kind = source.GetValueKind(path);
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
				ExpandToDepthRecursive(source, child.Path, currentDepth + 1, maxDepth);
			}
		}
	}

	private void FlattenFromSource(
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
		int id = _nextId++;
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
			FlattenFromSource(source, child.Path, child.PropertyName, child.ArrayIndex,
				child.ValueKind, child.RawValue, child.ChildCount, depth + 1, !isLast, result);
			i++;
		}

		// Add closing bracket
		result.Add(new FlattenedJsonNode
		{
			Id = _nextId++,
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
