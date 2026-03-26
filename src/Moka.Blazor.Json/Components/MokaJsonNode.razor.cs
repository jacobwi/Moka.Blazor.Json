using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Renders a single row in the JSON tree view. Stateless — receives all data via parameters.
/// </summary>
public sealed partial class MokaJsonNode : ComponentBase
{
	#region Parameters

	/// <summary>
	///     The flattened node data to render.
	/// </summary>
	[Parameter]
	public FlattenedJsonNode Node { get; set; }

	/// <summary>
	///     Whether this node is currently selected.
	/// </summary>
	[Parameter]
	public bool IsSelected { get; set; }

	/// <summary>
	///     Callback when the expand/collapse toggle is clicked.
	/// </summary>
	[Parameter]
	public EventCallback<string> OnToggle { get; set; }

	/// <summary>
	///     Callback when the node row is clicked (selection).
	/// </summary>
	[Parameter]
	public EventCallback<string> OnSelect { get; set; }

	/// <summary>
	///     Callback when the context menu is requested.
	/// </summary>
	[Parameter]
	public EventCallback<(string Path, double ClientX, double ClientY)> OnContextMenu { get; set; }

	/// <summary>
	///     Callback when the node is double-clicked (edit mode).
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
	///     Whether to show the child count (e.g. "13 items") on collapsed containers.
	/// </summary>
	[Parameter]
	public bool ShowChildCount { get; set; } = true;

	/// <summary>
	///     Whether the viewer is in read-only mode.
	/// </summary>
	[Parameter]
	public bool ReadOnly { get; set; }

	/// <summary>
	///     Active inline edit state from the parent viewer.
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

	#endregion

	#region Computed Properties

	private string ElementId => $"moka-node-{Node.Id}";
	private string EditInputId => $"moka-edit-{Node.Id}";

	private bool IsEditingValue => EditState is not null
	                               && EditState.Path == Node.Path
	                               && EditState.Target == InlineEditTarget.Value;

	private bool IsEditingKey => EditState is not null
	                             && EditState.Path == Node.Path
	                             && EditState.Target == InlineEditTarget.Key;

	private string CssClass
	{
		get
		{
			string css = "moka-json-node";
			if (IsSelected)
			{
				css += " moka-json-node--selected";
			}

			if (Node.IsSearchMatch)
			{
				css += " moka-json-node--search-match";
			}

			if (Node.IsActiveSearchMatch)
			{
				css += " moka-json-node--search-active";
			}

			if (!ReadOnly && !Node.IsClosingBracket)
			{
				css += " moka-json-node--editable";
			}

			return css;
		}
	}

	private string ValueCssClass => Node.ValueKind switch
	{
		JsonValueKind.String => "moka-json-value--string",
		JsonValueKind.Number => "moka-json-value--number",
		JsonValueKind.True or JsonValueKind.False => "moka-json-value--boolean",
		JsonValueKind.Null => "moka-json-value--null",
		_ => ""
	};

	private string GetCollapsedPreview()
	{
		if (Node.ChildCount == 0)
		{
			return Node.ValueKind == JsonValueKind.Object ? "" : "";
		}

		string itemWord = Node.ChildCount == 1 ? "item" : "items";
		return $" {Node.ChildCount} {itemWord} ";
	}

	private string ToggleChar => (ToggleStyle, Node.IsExpanded) switch
	{
		(MokaJsonToggleStyle.Triangle, true) => "\u25BC", // ▼
		(MokaJsonToggleStyle.Triangle, false) => "\u25B6", // ▶
		(MokaJsonToggleStyle.Chevron, true) => "\u2304", // ⌄
		(MokaJsonToggleStyle.Chevron, false) => "\u203A", // ›
		(MokaJsonToggleStyle.PlusMinus, true) => "\u2212", // −
		(MokaJsonToggleStyle.PlusMinus, false) => "+",
		(MokaJsonToggleStyle.Arrow, true) => "\u25BD", // ▽
		(MokaJsonToggleStyle.Arrow, false) => "\u25B7", // ▷
		_ => Node.IsExpanded ? "\u25BC" : "\u25B6"
	};

	private string ToggleSizeCss => ToggleSize switch
	{
		MokaJsonToggleSize.ExtraSmall => "moka-json-toggle--xs",
		MokaJsonToggleSize.Small => "moka-json-toggle--sm",
		MokaJsonToggleSize.Medium => "moka-json-toggle--md",
		MokaJsonToggleSize.Large => "moka-json-toggle--lg",
		MokaJsonToggleSize.ExtraLarge => "moka-json-toggle--xl",
		_ => "moka-json-toggle--sm"
	};

	#endregion

	#region Event Handlers

	private async Task HandleToggle(MouseEventArgs _)
	{
		if (!Node.IsClosingBracket && Node.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
		{
			await OnToggle.InvokeAsync(Node.Path);
		}
	}

	private async Task HandleClick(MouseEventArgs _)
	{
		if (!Node.IsClosingBracket)
		{
			await OnSelect.InvokeAsync(Node.Path);
		}
	}

	private async Task HandleContextMenu(MouseEventArgs e)
	{
		if (!Node.IsClosingBracket)
		{
			await OnContextMenu.InvokeAsync((Node.Path, e.ClientX, e.ClientY));
		}
	}

	private async Task HandleDoubleClick(MouseEventArgs _)
	{
		if (!Node.IsClosingBracket)
		{
			await OnDoubleClick.InvokeAsync(Node.Path);
		}
	}

	private void HandleEditInput(ChangeEventArgs e)
	{
		if (EditState is not null)
		{
			EditState.CurrentValue = e.Value?.ToString() ?? "";
			EditState.ValidationError = null;
		}
	}

	private async Task HandleEditKeyDown(KeyboardEventArgs e)
	{
		if (e.Key == "Enter")
		{
			await OnEditCommit.InvokeAsync(new InlineEditResult(EditState?.CurrentValue ?? "", true));
		}
		else if (e.Key == "Escape")
		{
			await OnEditCancel.InvokeAsync();
		}
	}

	private async Task HandleEditBlur()
	{
		// Commit on blur
		if (EditState is not null)
		{
			await OnEditCommit.InvokeAsync(new InlineEditResult(EditState.CurrentValue, true));
		}
	}

	private async Task HandleBoolSelect(ChangeEventArgs e)
	{
		string newValue = e.Value?.ToString() ?? "false";
		await OnEditCommit.InvokeAsync(new InlineEditResult(newValue, true));
	}

	#endregion

	#region Render Optimization

	private FlattenedJsonNode _previousNode;
	private bool _previousIsSelected;
	private bool _previousShowLineNumbers;
	private MokaJsonToggleStyle _previousToggleStyle;
	private MokaJsonToggleSize _previousToggleSize;
	private InlineEditState? _previousEditState;

	/// <inheritdoc />
	protected override bool ShouldRender()
	{
		bool editStateChanged = EditState != _previousEditState;
		if (Node != _previousNode || IsSelected != _previousIsSelected ||
		    ShowLineNumbers != _previousShowLineNumbers ||
		    ToggleStyle != _previousToggleStyle || ToggleSize != _previousToggleSize ||
		    editStateChanged)
		{
			_previousNode = Node;
			_previousIsSelected = IsSelected;
			_previousShowLineNumbers = ShowLineNumbers;
			_previousToggleStyle = ToggleStyle;
			_previousToggleSize = ToggleSize;
			_previousEditState = EditState;
			return true;
		}

		return false;
	}

	#endregion
}
