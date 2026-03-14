using System.Text;
using System.Text.Json;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Sorts object keys in a JSON element, producing a new JSON string with keys in alphabetical order.
/// </summary>
internal static class JsonSorter
{
	/// <summary>
	///     Returns a JSON string with the object keys at the top level sorted alphabetically.
	///     Non-object elements are returned as-is.
	/// </summary>
	/// <param name="element">The JSON element to sort.</param>
	/// <param name="indented">Whether to pretty-print the output.</param>
	/// <returns>A JSON string with sorted keys.</returns>
	public static string SortKeys(JsonElement element, bool indented = true)
	{
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented });
		WriteSorted(element, writer, false);
		writer.Flush();
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	/// <summary>
	///     Returns a JSON string with all object keys sorted recursively at every level.
	/// </summary>
	/// <param name="element">The JSON element to sort.</param>
	/// <param name="indented">Whether to pretty-print the output.</param>
	/// <returns>A JSON string with recursively sorted keys.</returns>
	public static string SortKeysRecursive(JsonElement element, bool indented = true)
	{
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented });
		WriteSorted(element, writer, true);
		writer.Flush();
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	private static void WriteSorted(JsonElement element, Utf8JsonWriter writer, bool recursive)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();

				var properties = element.EnumerateObject()
					.OrderBy(p => p.Name, StringComparer.Ordinal)
					.ToList();

				foreach (JsonProperty prop in properties)
				{
					writer.WritePropertyName(prop.Name);
					if (recursive)
					{
						WriteSorted(prop.Value, writer, true);
					}
					else
					{
						prop.Value.WriteTo(writer);
					}
				}

				writer.WriteEndObject();
				break;

			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (JsonElement item in element.EnumerateArray())
				{
					if (recursive)
					{
						WriteSorted(item, writer, true);
					}
					else
					{
						item.WriteTo(writer);
					}
				}

				writer.WriteEndArray();
				break;

			default:
				element.WriteTo(writer);
				break;
		}
	}
}
