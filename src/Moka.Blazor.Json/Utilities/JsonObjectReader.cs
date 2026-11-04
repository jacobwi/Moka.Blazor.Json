using System.Text.Json;
using System.Text.Json.Nodes;

namespace Moka.Blazor.Json.Utilities;

/// <summary>
///     Reads JSON objects the way consumers see them when a key appears more than once.
/// </summary>
/// <remarks>
///     System.Text.Json enumerates every copy of a repeated key, while <see cref="JsonElement.TryGetProperty(string, out JsonElement)" />
///     and browsers' <c>JSON.parse</c> resolve it to the last one. These helpers collapse repeats to one entry
///     at the first position holding the last value, so rows, child counts and path lookups agree.
/// </remarks>
internal static class JsonObjectReader
{
	private const int LinearScanLimit = 8;
	private const int MaxPooledScopeSize = 1024;

	/// <summary>
	///     Gets the object's properties with repeated keys collapsed.
	/// </summary>
	public static List<KeyValuePair<string, JsonElement>> GetProperties(JsonElement obj)
	{
		var result = new List<KeyValuePair<string, JsonElement>>();
		Dictionary<string, int>? positions = null;

		foreach (JsonProperty prop in obj.EnumerateObject())
		{
			string name = prop.Name;
			int existing = IndexOf(result, ref positions, name);
			if (existing >= 0)
			{
				result[existing] = new KeyValuePair<string, JsonElement>(name, prop.Value);
				continue;
			}

			positions?.Add(name, result.Count);
			result.Add(new KeyValuePair<string, JsonElement>(name, prop.Value));
		}

		return result;
	}

	/// <summary>
	///     Counts the object's distinct keys.
	/// </summary>
	public static int CountProperties(JsonElement obj, bool mayContainDuplicates)
	{
		if (!mayContainDuplicates)
		{
			return obj.GetPropertyCount();
		}

		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty prop in obj.EnumerateObject())
		{
			names.Add(prop.Name);
		}

		return names.Count;
	}

	/// <summary>
	///     Gets the child count of an object or array, 0 for primitives.
	/// </summary>
	public static int CountChildren(JsonElement element, bool mayContainDuplicates) => element.ValueKind switch
	{
		JsonValueKind.Object => CountProperties(element, mayContainDuplicates),
		JsonValueKind.Array => element.GetArrayLength(),
		_ => 0
	};

	/// <summary>
	///     Builds a mutable copy of <paramref name="element" />. Unlike <c>JsonNode.Parse</c>, repeated keys
	///     don't throw. Primitive values stay backed by the source document, so serialize the result before
	///     that document is disposed, or pass a <see cref="JsonElement.Clone" />.
	/// </summary>
	public static JsonNode? ToMutableNode(JsonElement element)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				var obj = new JsonObject();
				foreach (KeyValuePair<string, JsonElement> prop in GetProperties(element))
				{
					obj[prop.Key] = ToMutableNode(prop.Value);
				}

				return obj;

			case JsonValueKind.Array:
				var arr = new JsonArray();
				foreach (JsonElement item in element.EnumerateArray())
				{
					arr.Add(ToMutableNode(item));
				}

				return arr;

			case JsonValueKind.Null:
				return null;

			default:
				return JsonValue.Create(element);
		}
	}

	/// <summary>
	///     Scans raw JSON for any object that repeats a key. False positives are possible (hash collisions,
	///     escaped names); false negatives are not, so a <c>false</c> result lets callers skip de-duplication.
	/// </summary>
	public static bool MayContainDuplicateKeys(ReadOnlySpan<byte> utf8Json)
	{
		var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
			MaxDepth = 256
		});

		var scopes = new List<HashSet<ulong>?>();
		var pool = new Stack<HashSet<ulong>>();

		try
		{
			while (reader.Read())
			{
				switch (reader.TokenType)
				{
					case JsonTokenType.StartObject:
						scopes.Add(pool.Count > 0 ? pool.Pop() : []);
						break;

					case JsonTokenType.StartArray:
						scopes.Add(null);
						break;

					case JsonTokenType.EndObject:
					case JsonTokenType.EndArray:
						HashSet<ulong>? scope = scopes[^1];
						scopes.RemoveAt(scopes.Count - 1);
						if (scope is not null && scope.Count <= MaxPooledScopeSize)
						{
							scope.Clear();
							pool.Push(scope);
						}

						break;

					case JsonTokenType.PropertyName:
						if (reader.ValueIsEscaped || !scopes[^1]!.Add(Fnv1a(reader.ValueSpan)))
						{
							return true;
						}

						break;
				}
			}
		}
		catch (JsonException)
		{
			return true;
		}

		return false;
	}

	private static int IndexOf(List<KeyValuePair<string, JsonElement>> props, ref Dictionary<string, int>? positions,
		string name)
	{
		if (positions is null)
		{
			if (props.Count < LinearScanLimit)
			{
				for (int i = 0; i < props.Count; i++)
				{
					if (string.Equals(props[i].Key, name, StringComparison.Ordinal))
					{
						return i;
					}
				}

				return -1;
			}

			positions = new Dictionary<string, int>(props.Count * 2, StringComparer.Ordinal);
			for (int i = 0; i < props.Count; i++)
			{
				positions[props[i].Key] = i;
			}
		}

		return positions.TryGetValue(name, out int index) ? index : -1;
	}

	private static ulong Fnv1a(ReadOnlySpan<byte> bytes)
	{
		ulong hash = 14695981039346656037UL;
		foreach (byte b in bytes)
		{
			hash = (hash ^ b) * 1099511628211UL;
		}

		return hash;
	}
}
