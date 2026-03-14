using System.Text.Json;

namespace Moka.Blazor.Json.Models;

/// <summary>
///     A flattened representation of a single JSON node used for virtualized rendering.
///     Each instance represents one visible row in the tree view.
/// </summary>
/// <remarks>
///     This is a readonly record struct to minimize allocations during tree traversal.
/// </remarks>
public readonly record struct FlattenedJsonNode()
{
    /// <summary>
    ///     Unique identifier for this node within the current document, used for <c>@key</c> directives.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    ///     The JSON Pointer path (RFC 6901) to this node.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    ///     The depth of this node in the tree (root = 0).
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>
    ///     The property name if this node is an object member; <c>null</c> for array elements or root.
    /// </summary>
    public string? PropertyName { get; init; }

    /// <summary>
    ///     The kind of JSON value at this node.
    /// </summary>
    public required JsonValueKind ValueKind { get; init; }

    /// <summary>
    ///     The raw string representation of the value for primitive types, or <c>null</c> for containers.
    /// </summary>
    public string? RawValue { get; init; }

    /// <summary>
    ///     The number of child elements (for objects: property count, for arrays: element count).
    ///     Zero for primitive values.
    /// </summary>
    public required int ChildCount { get; init; }

    /// <summary>
    ///     The index of this element within its parent array, or <c>-1</c> if not an array element.
    /// </summary>
    public int ArrayIndex { get; init; } = -1;

    /// <summary>
    ///     Whether this node is currently expanded (only meaningful for objects and arrays).
    /// </summary>
    public required bool IsExpanded { get; init; }

    /// <summary>
    ///     Whether this node is a closing bracket/brace for a collapsed container.
    /// </summary>
    public bool IsClosingBracket { get; init; }

    /// <summary>
    ///     Whether this node matches the current search query.
    /// </summary>
    public bool IsSearchMatch { get; init; }

    /// <summary>
    ///     Whether this node is the currently active/focused search match.
    /// </summary>
    public bool IsActiveSearchMatch { get; init; }

    /// <summary>
    ///     Whether a comma should be rendered after this node (i.e., it is not the last sibling).
    /// </summary>
    public required bool HasTrailingComma { get; init; }
}