namespace Moka.Blazor.Json.Models;

/// <summary>
///     Specifies how toolbar buttons are displayed.
/// </summary>
public enum MokaJsonToolbarMode
{
	/// <summary>
	///     Text labels only (default).
	/// </summary>
	Text,

	/// <summary>
	///     SVG icons only with tooltips.
	/// </summary>
	Icon,

	/// <summary>
	///     Both SVG icons and text labels.
	/// </summary>
	IconAndText
}
