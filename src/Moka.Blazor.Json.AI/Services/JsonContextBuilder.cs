using System.Text;
using System.Text.Json;
using Moka.Blazor.AI.Models;
using Moka.Blazor.AI.Services;
using Moka.Blazor.Json.Components;

namespace Moka.Blazor.Json.AI.Services;

/// <summary>
///     Extracts relevant JSON context for AI prompts, truncating large documents.
///     Implements <see cref="IAiContextBuilder" /> from the base Moka.Blazor.AI library.
/// </summary>
internal sealed class JsonContextBuilder : IAiContextBuilder
{
	private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

	private readonly Dictionary<string, object?> _scopes = new(StringComparer.OrdinalIgnoreCase);

	private MokaJsonViewer? _viewer;

	/// <inheritdoc />
	public string BuildContext(AiChatOptions options)
	{
		// Multi-source mode: if "sources" scope is set, combine all named sources
		if (_scopes.TryGetValue("sources", out object? sourcesObj)
		    && sourcesObj is Dictionary<string, string> sources
		    && sources.Count > 0)
		{
			return BuildMultiSourceContext(sources, options);
		}

		if (_viewer is null)
		{
			return "[No JSON viewer connected]";
		}

		return BuildContext(_viewer, options);
	}

	/// <inheritdoc />
	public void SetScope(string key, object? data) => _scopes[key] = data;

	/// <inheritdoc />
	public void ClearScope(string? key = null)
	{
		if (key is null)
		{
			_scopes.Clear();
		}
		else
		{
			_scopes.Remove(key);
		}
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<string, object?> GetScopes() => _scopes;

	private static string BuildMultiSourceContext(Dictionary<string, string> sources, AiChatOptions options)
	{
		int budgetPerSource = options.MaxContextChars / sources.Count;
		var sb = new StringBuilder();
		sb.AppendLine($"[{sources.Count} data sources provided]");
		sb.AppendLine();

		foreach ((string label, string json) in sources)
		{
			sb.AppendLine($"--- {label} ---");

			if (json.Length <= budgetPerSource)
			{
				sb.AppendLine(json);
			}
			else
			{
				sb.AppendLine(json[..budgetPerSource]);
				sb.AppendLine("...(truncated)");
			}

			sb.AppendLine();
		}

		return sb.ToString();
	}

	/// <summary>
	///     Sets the viewer instance to extract context from.
	/// </summary>
	internal void SetViewer(MokaJsonViewer? viewer) => _viewer = viewer;

	/// <summary>
	///     Builds a context string from the viewer's current state.
	/// </summary>
	public string BuildContext(MokaJsonViewer viewer, AiChatOptions options)
	{
		string json;
		try
		{
			json = viewer.GetJson();
		}
		catch
		{
			return "[Unable to read JSON from viewer]";
		}

		if (string.IsNullOrWhiteSpace(json))
		{
			return "[No JSON loaded]";
		}

		// Check if a path scope is set — if so, extract and return just that subtree
		if (_scopes.TryGetValue("path", out object? scopeData) && scopeData is string scopePath
		                                                       && !string.IsNullOrEmpty(scopePath))
		{
			string subtree = ExtractSubtree(json, scopePath, options.MaxContextChars);
			if (!string.IsNullOrEmpty(subtree))
			{
				return $"[Scoped to node: {scopePath}]\n{subtree}";
			}

			// Path invalid — fall through to full context
		}

		// Small enough to include fully
		if (json.Length <= options.MaxContextChars)
		{
			return json;
		}

		// Large document — build a structural summary + selected node context
		return BuildTruncatedContext(json, viewer.SelectedPath, options.MaxContextChars);
	}

	private static string BuildTruncatedContext(string json, string? selectedPath, int maxChars)
	{
		int budgetForStructure = maxChars * 2 / 3;
		int budgetForSelected = maxChars / 3;

		var parts = new List<string>();

		// 1. Structural summary (top-level keys, types, array lengths)
		string structure = BuildStructuralSummary(json, budgetForStructure);
		parts.Add($"[Document structure ({json.Length:N0} chars total)]:");
		parts.Add(structure);

		// 2. Selected node context if available
		if (selectedPath is not null)
		{
			string selectedContext = ExtractSubtree(json, selectedPath, budgetForSelected);
			if (!string.IsNullOrEmpty(selectedContext))
			{
				parts.Add($"\n[Selected node at {selectedPath}]:");
				parts.Add(selectedContext);
			}
		}

		return string.Join("\n", parts);
	}

	private static string BuildStructuralSummary(string json, int maxChars)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var summary = new StringBuilder();

			DescribeElement(doc.RootElement, summary, 0, 2);

			string result = summary.ToString();
			return result.Length > maxChars ? result[..maxChars] + "\n..." : result;
		}
		catch
		{
			return json[..Math.Min(json.Length, maxChars)] + "\n...";
		}
	}

	private static void DescribeElement(JsonElement element, StringBuilder sb, int depth, int maxDepth)
	{
		string indent = new(' ', depth * 2);

		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				sb.AppendLine($"{indent}{{");
				int propCount = 0;
				foreach (JsonProperty prop in element.EnumerateObject())
				{
					if (depth >= maxDepth)
					{
						sb.AppendLine($"{indent}  \"{prop.Name}\": {DescribeType(prop.Value)}");
					}
					else
					{
						sb.Append($"{indent}  \"{prop.Name}\": ");
						DescribeElement(prop.Value, sb, depth + 1, maxDepth);
					}

					propCount++;
					if (propCount > 20)
					{
						sb.AppendLine($"{indent}  ... ({element.EnumerateObject().Count() - 20} more properties)");
						break;
					}
				}

				sb.AppendLine($"{indent}}}");
				break;

			case JsonValueKind.Array:
				int len = element.GetArrayLength();
				if (depth >= maxDepth || len == 0)
				{
					sb.AppendLine($"Array[{len}]");
				}
				else
				{
					sb.AppendLine($"[  // {len} items");
					// Show first item as representative
					DescribeElement(element[0], sb, depth + 1, maxDepth);
					if (len > 1)
					{
						sb.AppendLine($"{indent}  // ... {len - 1} more items");
					}

					sb.AppendLine($"{indent}]");
				}

				break;

			default:
				sb.AppendLine(DescribeType(element));
				break;
		}
	}

	private static string DescribeType(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.String => $"\"{Truncate(element.GetString() ?? "", 50)}\"",
		JsonValueKind.Number => element.GetRawText(),
		JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
		JsonValueKind.Null => "null",
		JsonValueKind.Object => $"Object({element.EnumerateObject().Count()} properties)",
		JsonValueKind.Array => $"Array[{element.GetArrayLength()}]",
		_ => element.ValueKind.ToString()
	};

	private static string Truncate(string s, int max) =>
		s.Length <= max ? s : s[..max] + "...";

	private static string ExtractSubtree(string json, string jsonPointer, int maxChars)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			JsonElement? element = NavigatePointer(doc.RootElement, jsonPointer);
			if (element is null)
			{
				return "";
			}

			string raw = element.Value.GetRawText();
			if (raw.Length <= maxChars)
			{
				return raw;
			}

			// Pretty-print and truncate
			try
			{
				using var reparsed = JsonDocument.Parse(raw);
				string pretty = JsonSerializer.Serialize(reparsed, IndentedOptions);
				return pretty.Length > maxChars ? pretty[..maxChars] + "\n..." : pretty;
			}
			catch
			{
				return raw[..maxChars] + "...";
			}
		}
		catch
		{
			return "";
		}
	}

	private static JsonElement? NavigatePointer(JsonElement root, string pointer)
	{
		if (string.IsNullOrEmpty(pointer) || pointer == "/")
		{
			return root;
		}

		JsonElement current = root;
		string[] segments = pointer.TrimStart('/').Split('/');

		foreach (string segment in segments)
		{
			string decoded = segment.Replace("~1", "/").Replace("~0", "~");

			if (current.ValueKind == JsonValueKind.Object)
			{
				if (!current.TryGetProperty(decoded, out JsonElement child))
				{
					return null;
				}

				current = child;
			}
			else if (current.ValueKind == JsonValueKind.Array && int.TryParse(decoded, out int index))
			{
				if (index < 0 || index >= current.GetArrayLength())
				{
					return null;
				}

				current = current[index];
			}
			else
			{
				return null;
			}
		}

		return current;
	}
}
