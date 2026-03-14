namespace Moka.Blazor.Json.Abstractions;

/// <summary>
///     Options for controlling JSON search behavior.
/// </summary>
public sealed class JsonSearchOptions
{
    /// <summary>
    ///     Whether to perform case-sensitive matching. Default is <c>false</c>.
    /// </summary>
    public bool CaseSensitive { get; init; }

    /// <summary>
    ///     Whether the query should be interpreted as a regular expression. Default is <c>false</c>.
    /// </summary>
    public bool UseRegex { get; init; }

    /// <summary>
    ///     Whether to search property keys. Default is <c>true</c>.
    /// </summary>
    public bool SearchKeys { get; init; } = true;

    /// <summary>
    ///     Whether to search values. Default is <c>true</c>.
    /// </summary>
    public bool SearchValues { get; init; } = true;

    /// <summary>
    ///     Whether to use JSON Path query syntax (e.g., $.users[*].name). Default is <c>false</c>.
    /// </summary>
    public bool UseJsonPath { get; init; }
}