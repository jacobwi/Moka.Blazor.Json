using Moka.Blazor.Json.Utilities;

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

		string[] segments = JsonPointerHelper.Split(jsonPointer);

		string result = "$";

		foreach (string segment in segments)
		{
			// Unescape JSON Pointer encoding
			string unescaped = JsonPointerHelper.UnescapeSegment(segment);

			if (IsArrayIndex(unescaped))
			{
				result += $"[{unescaped}]";
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
			if (c is '.' or ' ' or '[' or ']' or '"' or '\'' or '/' or '\\')
			{
				return true;
			}
		}

		// A leading digit would read as an array index; all-digit segments are rendered as one.
		return char.IsAsciiDigit(propertyName[0]) && !IsArrayIndex(propertyName);
	}

	private static bool IsArrayIndex(string segment) => segment.Length > 0 && segment.All(char.IsAsciiDigit);

	private static string EscapeQuotes(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
