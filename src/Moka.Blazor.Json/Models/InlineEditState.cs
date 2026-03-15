using System.Text.Json;

namespace Moka.Blazor.Json.Models;

/// <summary>
///     Specifies whether an inline edit targets the value or the property key.
/// </summary>
public enum InlineEditTarget
{
	Value,
	Key
}

/// <summary>
///     Tracks the state of an active inline edit session.
/// </summary>
public sealed class InlineEditState
{
	public required string Path { get; init; }
	public required InlineEditTarget Target { get; init; }
	public required string OriginalValue { get; init; }
	public string CurrentValue { get; set; } = "";
	public string? ValidationError { get; set; }
	public JsonValueKind ValueKind { get; init; }
}
