namespace Moka.Blazor.Json.Services;

/// <summary>
///     Converts JSON Pointer (RFC 6901) paths to user-friendly dot notation.
/// </summary>
/// <remarks>
///     <para>Internal paths remain in JSON Pointer format (e.g., "/users/0/name").</para>
///     <para>Display paths use dot notation (e.g., "$.users[0].name").</para>
///     <para>Rules:</para>
///     <list type="bullet">
///         <item>Root = <c>$</c></item>
///         <item>Object properties: <c>$.name</c>, <c>$.config.maxDepth</c></item>
///         <item>Array elements: <c>$.users[0].name</c></item>
///         <item>Properties with special chars (dots, spaces, brackets): <c>$["special.key"]</c></item>
///     </list>
/// </remarks>
internal static class JsonPathConverter
{
	/// <summary>
	///     Converts a JSON Pointer path to dot notation for display.
	/// </summary>
	/// <param name="jsonPointer">A JSON Pointer path, e.g. "/users/0/name".</param>
	/// <returns>A dot notation string, e.g. "$.users[0].name".</returns>
	public static string ToDotNotation(string? jsonPointer)
	{
		if (string.IsNullOrEmpty(jsonPointer))
		{
			return "$";
		}

		string[] segments = jsonPointer.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0)
		{
			return "$";
		}

		string result = "$";

		foreach (string segment in segments)
		{
			// Unescape JSON Pointer encoding
			string unescaped = segment.Replace("~1", "/").Replace("~0", "~");

			if (int.TryParse(unescaped, out int index))
				// Array index
			{
				result += $"[{index}]";
			}
			else if (NeedsQuoting(unescaped))
				// Property with special characters
			{
				result += $"[\"{EscapeQuotes(unescaped)}\"]";
			}
			else
				// Normal property
			{
				result += $".{unescaped}";
			}
		}

		return result;
	}

	/// <summary>
	///     Determines whether a property name needs bracket notation due to special characters.
	/// </summary>
	private static bool NeedsQuoting(string propertyName)
	{
		if (propertyName.Length == 0)
		{
			return true;
		}

		foreach (char c in propertyName)
		{
			if (c == '.' || c == ' ' || c == '[' || c == ']' || c == '"' || c == '\'' || c == '/')
			{
				return true;
			}
		}

		// Check if first char is a digit (would look like array index)
		if (char.IsDigit(propertyName[0]))
			// Only quote if it's not purely numeric (pure numeric is handled as array index)
		{
			if (!int.TryParse(propertyName, out _))
			{
				return true;
			}
		}

		return false;
	}

	private static string EscapeQuotes(string value) => value.Replace("\"", "\\\"");
}
