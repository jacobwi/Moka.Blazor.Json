using System.Text.Json;
using Moka.Blazor.Json.Abstractions;

namespace Moka.Blazor.Json.Models;

/// <summary>
///     Contextual information about the node on which a context menu action was invoked.
/// </summary>
public sealed class MokaJsonNodeContext
{
	/// <summary>
	///     The JSON Pointer path (RFC 6901) to this node, e.g. "/users/0/name".
	/// </summary>
	public required string Path { get; init; }

	/// <summary>
	///     The depth of this node in the tree (root = 0).
	/// </summary>
	public required int Depth { get; init; }

	/// <summary>
	///     The kind of JSON value (Object, Array, String, Number, True, False, Null, Undefined).
	/// </summary>
	public required JsonValueKind ValueKind { get; init; }

	/// <summary>
	///     The property name if this node is an object member; <c>null</c> for array elements or root.
	/// </summary>
	public string? PropertyName { get; init; }

	/// <summary>
	///     The raw JSON text of this node's value (truncated for large subtrees).
	/// </summary>
	public required string RawValuePreview { get; init; }

	/// <summary>
	///     The parent viewer component, allowing programmatic access.
	/// </summary>
	public required IMokaJsonViewer Viewer { get; init; }
}
