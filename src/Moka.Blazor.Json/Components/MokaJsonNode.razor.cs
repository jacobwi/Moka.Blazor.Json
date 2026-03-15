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

	#endregion

	#region Computed Properties

	private string ElementId => $"moka-node-{Node.Id}";

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
		MokaJsonToggleSize.Small => "moka-json-toggle--sm",
		MokaJsonToggleSize.Medium => "moka-json-toggle--md",
		MokaJsonToggleSize.Large => "moka-json-toggle--lg",
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

	#endregion

	#region Render Optimization

	private FlattenedJsonNode _previousNode;
	private bool _previousIsSelected;
	private bool _previousShowLineNumbers;
	private MokaJsonToggleStyle _previousToggleStyle;
	private MokaJsonToggleSize _previousToggleSize;

	/// <inheritdoc />
	protected override bool ShouldRender()
	{
		if (Node != _previousNode || IsSelected != _previousIsSelected ||
		    ShowLineNumbers != _previousShowLineNumbers ||
		    ToggleStyle != _previousToggleStyle || ToggleSize != _previousToggleSize)
		{
			_previousNode = Node;
			_previousIsSelected = IsSelected;
			_previousShowLineNumbers = ShowLineNumbers;
			_previousToggleStyle = ToggleStyle;
			_previousToggleSize = ToggleSize;
			return true;
		}

		return false;
	}

	#endregion
}
