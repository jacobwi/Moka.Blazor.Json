namespace Moka.Blazor.Json.Models;

/// <summary>
///     Event arguments raised when the JSON content is modified in edit mode.
/// </summary>
public sealed class JsonChangeEventArgs : EventArgs
{
    /// <summary>
    ///     The JSON Pointer path (RFC 6901) of the modified node.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    ///     The type of change that occurred.
    /// </summary>
    public required JsonChangeType ChangeType { get; init; }

    /// <summary>
    ///     The previous raw JSON value (before the change), or <c>null</c> for additions.
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    ///     The new raw JSON value (after the change), or <c>null</c> for deletions.
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    ///     The full updated JSON document as a string.
    /// </summary>
    public required string FullJson { get; init; }
}

/// <summary>
///     The type of modification made to the JSON document.
/// </summary>
public enum JsonChangeType
{
    /// <summary>A value was modified.</summary>
    ValueChanged,

    /// <summary>A new property or array element was added.</summary>
    NodeAdded,

    /// <summary>A property or array element was removed.</summary>
    NodeRemoved,

    /// <summary>A property was renamed.</summary>
    KeyRenamed,

    /// <summary>Object keys were sorted.</summary>
    KeysSorted
}