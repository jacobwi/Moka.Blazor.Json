namespace Moka.Blazor.Json.Models;

/// <summary>
///     Specifies the theme mode for the JSON viewer.
/// </summary>
public enum MokaJsonTheme
{
	/// <summary>
	///     Automatically matches the user's system preference via <c>prefers-color-scheme</c>.
	/// </summary>
	Auto,

	/// <summary>
	///     Forces the light color scheme.
	/// </summary>
	Light,

	/// <summary>
	///     Forces the dark color scheme.
	/// </summary>
	Dark,

	/// <summary>
	///     Applies no theme attributes — the component fully inherits parent CSS custom properties.
	/// </summary>
	Inherit
}
