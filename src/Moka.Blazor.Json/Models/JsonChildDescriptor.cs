using System.Text.Json;

namespace Moka.Blazor.Json.Models;

/// <summary>
///     Lightweight descriptor for a child node within a JSON container,
///     avoiding full <see cref="JsonElement" /> materialization for lazy sources.
/// </summary>
internal readonly record struct JsonChildDescriptor(
	string Path,
	string? PropertyName,
	int? ArrayIndex,
	JsonValueKind ValueKind,
	string? RawValue,
	int ChildCount);
