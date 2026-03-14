using Microsoft.AspNetCore.Components;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Toggleable toolbar providing search, collapse/expand, format, and copy actions.
/// </summary>
public sealed partial class MokaJsonToolbar : ComponentBase
{
	/// <summary>Callback to toggle the search overlay.</summary>
	[Parameter]
	public EventCallback OnSearchToggle { get; set; }

	/// <summary>Callback to expand all nodes.</summary>
	[Parameter]
	public EventCallback OnExpandAll { get; set; }

	/// <summary>Callback to collapse all nodes.</summary>
	[Parameter]
	public EventCallback OnCollapseAll { get; set; }

	/// <summary>Callback to toggle between formatted and minified display.</summary>
	[Parameter]
	public EventCallback OnFormatToggle { get; set; }

	/// <summary>Callback to copy all JSON to clipboard.</summary>
	[Parameter]
	public EventCallback OnCopyAll { get; set; }

	/// <summary>Whether the JSON is currently formatted (pretty-printed).</summary>
	[Parameter]
	public bool IsFormatted { get; set; } = true;

	/// <summary>Optional extra content to render at the end of the toolbar.</summary>
	[Parameter]
	public RenderFragment? ToolbarExtra { get; set; }
}
