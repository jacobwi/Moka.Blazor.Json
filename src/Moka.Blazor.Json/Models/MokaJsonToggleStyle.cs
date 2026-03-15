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
	Large
}
