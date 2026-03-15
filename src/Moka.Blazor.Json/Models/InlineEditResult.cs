namespace Moka.Blazor.Json.Models;

/// <summary>
///     Result of an inline edit operation.
/// </summary>
public readonly record struct InlineEditResult(string NewValue, bool Committed);
