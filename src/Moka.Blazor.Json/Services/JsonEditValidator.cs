using System.Text.Json;
using System.Text.Json.Nodes;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Validates inline edit values for JSON nodes.
/// </summary>
internal static class JsonEditValidator
{
	public static string? ValidateValue(string input, JsonValueKind targetKind)
	{
		return targetKind switch
		{
			JsonValueKind.Number => IsJsonNumber(input) ? null : "Invalid number",
			JsonValueKind.String => null,
			JsonValueKind.True or JsonValueKind.False => input is "true" or "false"
				? null
				: "Must be true or false",
			_ => null
		};
	}

	/// <summary>
	///     Turns validated edit input into the JSON text to store. A null value takes whatever the input
	///     parses as (number, <c>true</c>/<c>false</c>, <c>null</c>, a quoted string, an object or an array);
	///     input that isn't valid JSON is stored as a string.
	/// </summary>
	public static string ToJsonLiteral(string input, JsonValueKind targetKind) => targetKind switch
	{
		JsonValueKind.String => ToJsonString(input),
		JsonValueKind.Number => input.Trim(),
		JsonValueKind.True or JsonValueKind.False => input == "true" ? "true" : "false",
		JsonValueKind.Null => IsJson(input) ? input.Trim() : ToJsonString(input),
		_ => input
	};

	private static string ToJsonString(string input)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStringValue(input);
		}

		return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	private static bool IsJsonNumber(string input)
	{
		// double.TryParse accepts "1,000", "NaN" and ".5", none of which are JSON numbers.
		try
		{
			using var doc = JsonDocument.Parse(input);
			return doc.RootElement.ValueKind == JsonValueKind.Number;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool IsJson(string input)
	{
		try
		{
			using var doc = JsonDocument.Parse(input);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static string? ValidatePropertyName(string name, JsonObject parentObj, string? currentName)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "Key cannot be empty";
		}

		if (name != currentName && parentObj.ContainsKey(name))
		{
			return "Duplicate key";
		}

		return null;
	}
}
