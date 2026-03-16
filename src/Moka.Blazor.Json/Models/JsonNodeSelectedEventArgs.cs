using System.Text.Json;

namespace Moka.Blazor.Json.Models;

/// <summary>
///     Event arguments raised when a node is selected in the JSON viewer.
/// </summary>
public sealed class JsonNodeSelectedEventArgs : EventArgs
{
	/// <summary>
	///     The JSON Pointer path (RFC 6901) to the selected node.
	/// </summary>
	public required string Path { get; init; }

	/// <summary>
	///     The depth of the selected node (root = 0).
	/// </summary>
	public required int Depth { get; init; }

	/// <summary>
	///     The kind of JSON value at the selected node.
	/// </summary>
	public required JsonValueKind ValueKind { get; init; }

	/// <summary>
	///     The property name if this node is an object member; <c>null</c> otherwise.
	/// </summary>
	public string? PropertyName { get; init; }

	/// <summary>
	///     The full raw JSON text of this node's value.
	/// </summary>
	public required string RawValue { get; init; }

	/// <summary>
	///     A preview of the raw JSON value (truncated for large values).
	/// </summary>
	public required string RawValuePreview { get; init; }
}
