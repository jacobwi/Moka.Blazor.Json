using Microsoft.AspNetCore.Components;

namespace Moka.Blazor.Json.Models;

/// <summary>
///     Inline SVG icons used by the toolbar and context menu.
///     All icons are 16x16, stroke-based, using currentColor for theming.
/// </summary>
internal static class MokaJsonIcons
{
	private const string Attrs =
		"class=\"moka-json-icon\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" width=\"1em\" height=\"1em\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";

	// ── Toolbar icons ──

	/// <summary>Magnifying glass.</summary>
	internal static readonly MarkupString Search = new(
		$"<svg {Attrs}><circle cx=\"6.5\" cy=\"6.5\" r=\"4\"/><line x1=\"9.5\" y1=\"9.5\" x2=\"14\" y2=\"14\"/></svg>");

	/// <summary>Expand outward arrows.</summary>
	internal static readonly MarkupString Expand = new(
		$"<svg {Attrs}><polyline points=\"10 2 14 2 14 6\"/><polyline points=\"6 14 2 14 2 10\"/><line x1=\"14\" y1=\"2\" x2=\"9\" y2=\"7\"/><line x1=\"2\" y1=\"14\" x2=\"7\" y2=\"9\"/></svg>");

	/// <summary>Collapse inward arrows.</summary>
	internal static readonly MarkupString Collapse = new(
		$"<svg {Attrs}><polyline points=\"4 10 4 14 8 14\"/><polyline points=\"12 6 12 2 8 2\"/><line x1=\"4\" y1=\"14\" x2=\"9\" y2=\"9\"/><line x1=\"12\" y1=\"2\" x2=\"7\" y2=\"7\"/></svg>");

	/// <summary>Braces — format/pretty-print.</summary>
	internal static readonly MarkupString Format = new(
		$"<svg {Attrs}><path d=\"M4 2C2.5 2 2 3 2 4v2c0 1-1 1.5-1 2s1 1 1 2v2c0 1 .5 2 2 2\"/><path d=\"M12 2c1.5 0 2 1 2 2v2c0 1 1 1.5 1 2s-1 1-1 2v2c0 1-.5 2-2 2\"/></svg>");

	/// <summary>Compact bars — minify.</summary>
	internal static readonly MarkupString Minify = new(
		$"<svg {Attrs}><line x1=\"2\" y1=\"4\" x2=\"14\" y2=\"4\"/><line x1=\"2\" y1=\"8\" x2=\"14\" y2=\"8\"/><line x1=\"2\" y1=\"12\" x2=\"10\" y2=\"12\"/></svg>");

	/// <summary>Clipboard — copy.</summary>
	internal static readonly MarkupString Copy = new(
		$"<svg {Attrs}><rect x=\"5\" y=\"5\" width=\"9\" height=\"9\" rx=\"1\"/><path d=\"M5 11H4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1h7a1 1 0 0 1 1 1v1\"/></svg>");

	/// <summary>Download arrow — export.</summary>
	internal static readonly MarkupString Export = new(
		$"<svg {Attrs}><path d=\"M8 2v8\"/><polyline points=\"4 7 8 11 12 7\"/><line x1=\"2\" y1=\"14\" x2=\"14\" y2=\"14\"/></svg>");

	// ── Context menu icons ──

	/// <summary>Path/route icon.</summary>
	internal static readonly MarkupString CopyPath = new(
		$"<svg {Attrs}><path d=\"M2 8h4l2-3 2 6 2-3h4\"/></svg>");

	/// <summary>Crosshair — scope to node.</summary>
	internal static readonly MarkupString Scope = new(
		$"<svg {Attrs}><circle cx=\"8\" cy=\"8\" r=\"4\"/><line x1=\"8\" y1=\"1\" x2=\"8\" y2=\"3\"/><line x1=\"8\" y1=\"13\" x2=\"8\" y2=\"15\"/><line x1=\"1\" y1=\"8\" x2=\"3\" y2=\"8\"/><line x1=\"13\" y1=\"8\" x2=\"15\" y2=\"8\"/></svg>");

	/// <summary>Sort A-Z arrows.</summary>
	internal static readonly MarkupString Sort = new(
		$"<svg {Attrs}><path d=\"M4 2v12\"/><polyline points=\"1 11 4 14 7 11\"/><line x1=\"9\" y1=\"4\" x2=\"15\" y2=\"4\"/><line x1=\"9\" y1=\"8\" x2=\"13\" y2=\"8\"/><line x1=\"9\" y1=\"12\" x2=\"11\" y2=\"12\"/></svg>");

	/// <summary>Pencil — edit value.</summary>
	internal static readonly MarkupString Edit = new(
		$"<svg {Attrs}><path d=\"M11.5 1.5l3 3L5 14H2v-3z\"/></svg>");

	/// <summary>Tag — rename key.</summary>
	internal static readonly MarkupString Rename = new(
		$"<svg {Attrs}><path d=\"M1 8V4a1 1 0 0 1 1-1h4l8 8-4 4z\"/><circle cx=\"5\" cy=\"6\" r=\"1\" fill=\"currentColor\" stroke=\"none\"/></svg>");

	/// <summary>Trash can — delete.</summary>
	internal static readonly MarkupString Delete = new(
		$"<svg {Attrs}><line x1=\"2\" y1=\"4\" x2=\"14\" y2=\"4\"/><path d=\"M5 4V2h6v2\"/><path d=\"M3 4l1 10a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1l1-10\"/></svg>");

	/// <summary>Plus in circle — add.</summary>
	internal static readonly MarkupString Add = new(
		$"<svg {Attrs}><circle cx=\"8\" cy=\"8\" r=\"6\"/><line x1=\"8\" y1=\"5\" x2=\"8\" y2=\"11\"/><line x1=\"5\" y1=\"8\" x2=\"11\" y2=\"8\"/></svg>");

	/// <summary>Gear — settings.</summary>
	internal static readonly MarkupString Settings = new(
		$"<svg {Attrs}><circle cx=\"8\" cy=\"8\" r=\"2.5\"/><path d=\"M8 1.5v1.2M8 13.3v1.2M1.5 8h1.2M13.3 8h1.2M3.4 3.4l.85.85M11.75 11.75l.85.85M3.4 12.6l.85-.85M11.75 4.25l.85-.85\"/></svg>");

	/// <summary>
	///     Returns the SVG string (without MarkupString wrapper) for use in <see cref="MokaJsonContextAction.IconSvg" />.
	/// </summary>
	internal static string SvgString(MarkupString icon) => icon.Value;
}
