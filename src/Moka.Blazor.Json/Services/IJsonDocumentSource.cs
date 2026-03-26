using System.Text.Json;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Services;

/// <summary>
///     Abstraction over a parsed JSON document, enabling both eager (<see cref="JsonDocument" />)
///     and lazy (byte-offset indexed) implementations.
/// </summary>
internal interface IJsonDocumentSource : IAsyncDisposable
{
	/// <summary>
	///     Gets whether a document is currently loaded and ready for queries.
	/// </summary>
	bool IsLoaded { get; }

	/// <summary>
	///     Gets the size of the document in bytes (UTF-8 encoded).
	/// </summary>
	long DocumentSizeBytes { get; }

	/// <summary>
	///     Gets how long the initial parse/index took.
	/// </summary>
	TimeSpan ParseTime { get; }

	/// <summary>
	///     Gets the <see cref="JsonValueKind" /> of the root element.
	/// </summary>
	JsonValueKind RootValueKind { get; }

	/// <summary>
	///     Gets whether this source supports editing operations (value replace, delete, add, rename).
	///     Lazy sources return <c>false</c>.
	/// </summary>
	bool SupportsEditing { get; }

	/// <summary>
	///     Gets the child count for the container at the given JSON Pointer path.
	///     Returns 0 for primitives. Returns -1 if the count is unknown and would require
	///     an expensive scan (lazy sources may return this for deep containers).
	/// </summary>
	int GetChildCount(string path);

	/// <summary>
	///     Enumerates the immediate children of the container at the given JSON Pointer path.
	///     Each child is described as a lightweight <see cref="JsonChildDescriptor" />.
	/// </summary>
	IEnumerable<JsonChildDescriptor> EnumerateChildren(string path);

	/// <summary>
	///     Gets the raw string representation of a primitive node at the given path.
	///     Returns <c>null</c> for containers (objects/arrays).
	/// </summary>
	string? GetRawValue(string path);

	/// <summary>
	///     Gets the <see cref="JsonValueKind" /> of the node at the given path.
	/// </summary>
	JsonValueKind GetValueKind(string path);

	/// <summary>
	///     Gets the <see cref="JsonElement" /> at the given path. For lazy implementations,
	///     this parses the subtree on demand and may cache the result.
	/// </summary>
	JsonElement GetElement(string path);

	/// <summary>
	///     Returns the full JSON as a string.
	/// </summary>
	string GetJsonString(bool indented = true);

	/// <summary>
	///     Counts all nodes in the document tree. Returns -1 if the count is unavailable
	///     without a full traversal (lazy sources).
	/// </summary>
	int CountNodes();

	/// <summary>
	///     Gets the maximum depth of the document tree. Returns -1 if unavailable
	///     without a full traversal (lazy sources).
	/// </summary>
	int GetMaxDepth();
}
