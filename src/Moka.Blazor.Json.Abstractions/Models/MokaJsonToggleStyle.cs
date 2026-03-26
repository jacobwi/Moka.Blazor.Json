namespace Moka.Blazor.Json.Models;

/// <summary>
///     Specifies the style of expand/collapse toggle indicators in the JSON tree.
/// </summary>
public enum MokaJsonToggleStyle
{
	/// <summary>
	///     Solid triangles: ▶ (collapsed) / ▼ (expanded). Default.
	/// </summary>
	Triangle,

	/// <summary>
	///     Chevron arrows: › (collapsed) / ⌄ (expanded).
	/// </summary>
	Chevron,

	/// <summary>
	///     Plus/minus signs: + (collapsed) / − (expanded).
	/// </summary>
	PlusMinus,

	/// <summary>
	///     Outline arrows: ▷ (collapsed) / ▽ (expanded).
	/// </summary>
	Arrow
}

/// <summary>
///     Specifies the size of expand/collapse toggle indicators.
/// </summary>
public enum MokaJsonToggleSize
{
	/// <summary>
	///     Extra-small toggle (6px font, 12px button).
	/// </summary>
	ExtraSmall,

	/// <summary>
	///     Small toggle (8px font, 14px button).
	/// </summary>
	Small,

	/// <summary>
	///     Medium toggle (10px font, 16px button). Default.
	/// </summary>
	Medium,

	/// <summary>
	///     Large toggle (13px font, 20px button).
	/// </summary>
	Large,

	/// <summary>
	///     Extra-large toggle (16px font, 24px button).
	/// </summary>
	ExtraLarge
}

/// <summary>
///     Specifies the initial collapse behavior when a document is loaded.
/// </summary>
public enum MokaJsonCollapseMode
{
	/// <summary>
	///     Expand to <c>MaxDepthExpanded</c> depth (default behavior).
	/// </summary>
	Depth,

	/// <summary>
	///     Collapse everything — show only the root brackets <c>{ }</c> or <c>[ ]</c>.
	/// </summary>
	Root,

	/// <summary>
	///     Expand all nodes in the tree.
	/// </summary>
	Expanded
}
