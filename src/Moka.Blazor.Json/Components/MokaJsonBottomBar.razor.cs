using Microsoft.AspNetCore.Components;
using Moka.Blazor.Json.Services;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Status bar displaying document statistics, selection path, and validation status.
/// </summary>
public sealed partial class MokaJsonBottomBar : ComponentBase
{
	/// <summary>Formatted document size string (e.g., "14.2 KB").</summary>
	[Parameter]
	public string? DocumentSize { get; set; }

	/// <summary>Total number of nodes in the document.</summary>
	[Parameter]
	public int NodeCount { get; set; }

	/// <summary>The depth of the currently selected node.</summary>
	[Parameter]
	public int CurrentDepth { get; set; }

	/// <summary>The maximum depth of the document.</summary>
	[Parameter]
	public int MaxDepth { get; set; }

	/// <summary>Formatted parse time string (e.g., "12.3 ms").</summary>
	[Parameter]
	public string? ParseTimeMs { get; set; }

	/// <summary>The currently selected node's JSON Pointer path.</summary>
	[Parameter]
	public string? SelectedPath { get; set; }

	/// <summary>Whether the JSON document is valid.</summary>
	[Parameter]
	public bool IsValid { get; set; } = true;

	/// <summary>Validation error message, if any.</summary>
	[Parameter]
	public string? ValidationError { get; set; }

	/// <summary>Whether the document is loaded in lazy (indexed) mode.</summary>
	[Parameter]
	public bool IsLazyMode { get; set; }

	private string ValidationClass => IsValid
		? "moka-json-bottom-bar-item moka-json-validation-valid"
		: "moka-json-bottom-bar-item moka-json-validation-invalid";

	private string ValidationText => IsValid ? "Valid JSON" : $"Invalid: {ValidationError}";

	private string DisplayPath => JsonPathConverter.ToDotNotation(SelectedPath);
}
