namespace Moka.Blazor.Json.Utilities;

/// <summary>
///     Provides RFC 6901 JSON Pointer escape/unescape helpers.
/// </summary>
internal static class JsonPointerHelper
{
	/// <summary>
	///     Escapes a single JSON Pointer segment: replaces <c>~</c> with <c>~0</c> then <c>/</c> with <c>~1</c>.
	/// </summary>
	public static string EscapeSegment(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

	/// <summary>
	///     Unescapes a single JSON Pointer segment: replaces <c>~1</c> with <c>/</c> then <c>~0</c> with <c>~</c>.
	/// </summary>
	public static string UnescapeSegment(string segment) => segment.Replace("~1", "/").Replace("~0", "~");
}
