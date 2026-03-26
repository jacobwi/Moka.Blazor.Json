using Microsoft.AspNetCore.Components;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Dropdown settings panel for the JSON viewer.
///     Allows runtime configuration of display, layout, behavior, and search options.
/// </summary>
public sealed partial class MokaJsonSettingsPanel : ComponentBase
{
	private readonly string _id = Guid.NewGuid().ToString("N")[..8];

	#region Visibility & Position

	/// <summary>Whether the panel is visible.</summary>
	[Parameter]
	public bool IsVisible { get; set; }

	/// <summary>X position (pixels from left).</summary>
	[Parameter]
	public double Left { get; set; }

	/// <summary>Y position (pixels from top).</summary>
	[Parameter]
	public double Top { get; set; }

	/// <summary>Callback to dismiss/close the panel.</summary>
	[Parameter]
	public EventCallback OnDismiss { get; set; }

	private string PanelStyle => $"left:{Left}px;top:{Top}px;";

	#endregion

	#region Display Settings

	/// <summary>Current theme.</summary>
	[Parameter]
	public MokaJsonTheme Theme { get; set; }

	[Parameter] public EventCallback<MokaJsonTheme> ThemeChanged { get; set; }

	/// <summary>Current toolbar display mode.</summary>
	[Parameter]
	public MokaJsonToolbarMode ToolbarMode { get; set; }

	[Parameter] public EventCallback<MokaJsonToolbarMode> ToolbarModeChanged { get; set; }

	/// <summary>Current toggle style.</summary>
	[Parameter]
	public MokaJsonToggleStyle ToggleStyle { get; set; }

	[Parameter] public EventCallback<MokaJsonToggleStyle> ToggleStyleChanged { get; set; }

	/// <summary>Current toggle size.</summary>
	[Parameter]
	public MokaJsonToggleSize ToggleSize { get; set; }

	[Parameter] public EventCallback<MokaJsonToggleSize> ToggleSizeChanged { get; set; }

	#endregion

	#region Layout Settings

	/// <summary>Whether line numbers are shown.</summary>
	[Parameter]
	public bool ShowLineNumbers { get; set; }

	[Parameter] public EventCallback<bool> ShowLineNumbersChanged { get; set; }

	/// <summary>Whether word wrap is enabled.</summary>
	[Parameter]
	public bool WordWrap { get; set; }

	[Parameter] public EventCallback<bool> WordWrapChanged { get; set; }

	/// <summary>Whether the breadcrumb is shown.</summary>
	[Parameter]
	public bool ShowBreadcrumb { get; set; }

	[Parameter] public EventCallback<bool> ShowBreadcrumbChanged { get; set; }

	/// <summary>Whether the bottom bar is shown.</summary>
	[Parameter]
	public bool ShowBottomBar { get; set; }

	[Parameter] public EventCallback<bool> ShowBottomBarChanged { get; set; }

	#endregion

	#region Behavior Settings

	/// <summary>Max depth to expand.</summary>
	[Parameter]
	public int MaxDepthExpanded { get; set; }

	[Parameter] public EventCallback<int> MaxDepthExpandedChanged { get; set; }

	/// <summary>Collapse mode.</summary>
	[Parameter]
	public MokaJsonCollapseMode CollapseMode { get; set; }

	[Parameter] public EventCallback<MokaJsonCollapseMode> CollapseModeChanged { get; set; }

	/// <summary>Whether the viewer is read-only.</summary>
	[Parameter]
	public bool ReadOnly { get; set; }

	[Parameter] public EventCallback<bool> ReadOnlyChanged { get; set; }

	#endregion

	#region Search Settings

	/// <summary>Default case-sensitive state for search.</summary>
	[Parameter]
	public bool SearchCaseSensitive { get; set; }

	[Parameter] public EventCallback<bool> SearchCaseSensitiveChanged { get; set; }

	/// <summary>Default regex mode for search.</summary>
	[Parameter]
	public bool SearchUseRegex { get; set; }

	[Parameter] public EventCallback<bool> SearchUseRegexChanged { get; set; }

	#endregion

	#region Helpers

	private static string RadioClass(bool selected) =>
		selected ? "moka-json-settings-radio-opt selected" : "moka-json-settings-radio-opt";

	private Task HandleToggleSizeChange(ChangeEventArgs e) =>
		Enum.TryParse((string?)e.Value, out MokaJsonToggleSize size)
			? ToggleSizeChanged.InvokeAsync(size)
			: Task.CompletedTask;

	private Task HandleDepthChange(ChangeEventArgs e) =>
		int.TryParse((string?)e.Value, out int depth) && depth >= 0
			? MaxDepthExpandedChanged.InvokeAsync(depth)
			: Task.CompletedTask;

	#endregion
}
