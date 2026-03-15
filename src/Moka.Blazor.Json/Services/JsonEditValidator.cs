using System.Globalization;
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
			JsonValueKind.Number => double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
				? null
				: "Invalid number",
			JsonValueKind.String => null,
			JsonValueKind.True or JsonValueKind.False => input is "true" or "false"
				? null
				: "Must be true or false",
			JsonValueKind.Null => input == "null" ? null : "Must be null",
			_ => null
		};
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
