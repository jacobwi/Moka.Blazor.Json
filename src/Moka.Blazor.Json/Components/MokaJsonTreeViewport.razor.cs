using Microsoft.AspNetCore.Components;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Virtualized scrolling container that renders <see cref="MokaJsonNode" /> rows.
///     Uses Blazor's built-in <c>Virtualize</c> component for performance.
/// </summary>
public sealed partial class MokaJsonTreeViewport : ComponentBase
{
	/// <summary>
	///     The flattened list of nodes to render.
	/// </summary>
	[Parameter]
	public List<FlattenedJsonNode>? Nodes { get; set; }

	/// <summary>
	///     The currently selected node path.
	/// </summary>
	[Parameter]
	public string? SelectedPath { get; set; }

	/// <summary>
	///     Callback when a node's expand/collapse toggle is clicked.
	/// </summary>
	[Parameter]
	public EventCallback<string> OnToggle { get; set; }

	/// <summary>
	///     Callback when a node is clicked (selected).
	/// </summary>
	[Parameter]
	public EventCallback<string> OnSelect { get; set; }

	/// <summary>
	///     Callback when the context menu is requested on a node.
	/// </summary>
	[Parameter]
	public EventCallback<(string Path, double ClientX, double ClientY)> OnContextMenu { get; set; }

	/// <summary>
	///     Callback when a node is double-clicked.
	/// </summary>
	[Parameter]
	public EventCallback<string> OnDoubleClick { get; set; }

	/// <summary>
	///     Whether to show line numbers in the gutter.
	/// </summary>
	[Parameter]
	public bool ShowLineNumbers { get; set; }

	/// <summary>
	///     Style of expand/collapse toggle indicators.
	/// </summary>
	[Parameter]
	public MokaJsonToggleStyle ToggleStyle { get; set; }

	/// <summary>
	///     Size of expand/collapse toggle indicators.
	/// </summary>
	[Parameter]
	public MokaJsonToggleSize ToggleSize { get; set; }

	/// <summary>
	///     Active inline edit state, if any.
	/// </summary>
	[Parameter]
	public InlineEditState? EditState { get; set; }

	/// <summary>
	///     Callback when an inline edit is committed.
	/// </summary>
	[Parameter]
	public EventCallback<InlineEditResult> OnEditCommit { get; set; }

	/// <summary>
	///     Callback when an inline edit is cancelled.
	/// </summary>
	[Parameter]
	public EventCallback OnEditCancel { get; set; }

	/// <summary>
	///     Whether the viewer is in read-only mode.
	/// </summary>
	[Parameter]
	public bool ReadOnly { get; set; }

	private string ViewportId { get; } = $"moka-viewport-{Guid.NewGuid():N}";
}
