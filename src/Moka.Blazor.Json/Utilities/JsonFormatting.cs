using System.Text.Encodings.Web;
using System.Text.Json;

namespace Moka.Blazor.Json.Utilities;

internal static class JsonFormatting
{
	// The default encoder escapes all non-ASCII and HTML-sensitive characters (é becomes é),
	// which would rewrite every such string in the document on the first edit, copy or export.
	private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

	public static readonly JsonDocumentOptions LenientDocument = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip,
		MaxDepth = 256
	};

	public static readonly JsonSerializerOptions IndentedSerializer = new()
	{
		WriteIndented = true,
		Encoder = Encoder,
		MaxDepth = 256
	};

	private static readonly JsonWriterOptions IndentedWriter = new() { Indented = true, Encoder = Encoder };
	private static readonly JsonWriterOptions CompactWriter = new() { Encoder = Encoder };

	public static JsonWriterOptions Writer(bool indented) => indented ? IndentedWriter : CompactWriter;
}
