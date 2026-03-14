namespace Moka.Blazor.Json.Abstractions;

/// <summary>
///     Programmatic interface for controlling the JSON viewer component.
/// </summary>
public interface IMokaJsonViewer
{
    /// <summary>
    ///     Gets the currently selected node's JSON Pointer path, or null if nothing is selected.
    /// </summary>
    string? SelectedPath { get; }

    /// <summary>
    ///     Gets whether the viewer is currently in edit mode.
    /// </summary>
    bool IsEditing { get; }

    /// <summary>
    ///     Navigates to and selects the node at the specified JSON Pointer path (RFC 6901).
    /// </summary>
    /// <param name="jsonPointer">The JSON Pointer path, e.g. "/users/0/name".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask NavigateToAsync(string jsonPointer, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Expands all nodes up to the specified depth. Pass <c>-1</c> for unlimited.
    /// </summary>
    /// <param name="depth">Maximum depth to expand, or -1 for all.</param>
    void ExpandToDepth(int depth);

    /// <summary>
    ///     Collapses all nodes in the tree.
    /// </summary>
    void CollapseAll();

    /// <summary>
    ///     Expands all nodes in the tree.
    /// </summary>
    void ExpandAll();

    /// <summary>
    ///     Initiates a search with the given query.
    /// </summary>
    /// <param name="query">The search query string.</param>
    /// <param name="options">Search options (regex, case sensitivity, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of matches found.</returns>
    ValueTask<int> SearchAsync(string query, JsonSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Navigates to the next search match.
    /// </summary>
    void NextMatch();

    /// <summary>
    ///     Navigates to the previous search match.
    /// </summary>
    void PreviousMatch();

    /// <summary>
    ///     Clears the current search results and highlighting.
    /// </summary>
    void ClearSearch();

    /// <summary>
    ///     Undoes the last edit operation. Only available when editing is enabled.
    /// </summary>
    void Undo();

    /// <summary>
    ///     Redoes the last undone edit operation. Only available when editing is enabled.
    /// </summary>
    void Redo();

    /// <summary>
    ///     Gets the current JSON content as a formatted string.
    /// </summary>
    /// <param name="indented">Whether to pretty-print the output.</param>
    /// <returns>The JSON string.</returns>
    string GetJson(bool indented = true);
}