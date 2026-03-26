namespace Moka.Blazor.Json.Models;

/// <summary>
///     Defines a single action available in the JSON viewer context menu.
/// </summary>
public sealed class MokaJsonContextAction
{
	/// <summary>
	///     Unique identifier for this action.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>
	///     Display label shown in the menu.
	/// </summary>
	public required string Label { get; init; }

	/// <summary>
	///     Optional CSS class for an icon displayed next to the label.
	/// </summary>
	public string? IconCss { get; init; }

	/// <summary>
	///     Optional inline SVG markup for an icon displayed next to the label.
	///     Takes precedence over <see cref="IconCss" /> when both are set.
	/// </summary>
	public string? IconSvg { get; init; }

	/// <summary>
	///     Keyboard shortcut hint displayed in the menu (e.g., "Ctrl+C").
	/// </summary>
	public string? ShortcutHint { get; init; }

	/// <summary>
	///     Predicate that determines whether this action is visible for the given node.
	///     Return <c>false</c> to hide the action for certain node types or depths.
	/// </summary>
	public Func<MokaJsonNodeContext, bool>? IsVisible { get; init; }

	/// <summary>
	///     Predicate that determines whether this action is enabled (visible but grayed out if false).
	/// </summary>
	public Func<MokaJsonNodeContext, bool>? IsEnabled { get; init; }

	/// <summary>
	///     The callback invoked when the user selects this action.
	/// </summary>
	public required Func<MokaJsonNodeContext, ValueTask> OnExecute { get; init; }

	/// <summary>
	///     Sort order within the menu. Lower values appear first. Default is <c>0</c>.
	/// </summary>
	public int Order { get; init; }

	/// <summary>
	///     If <c>true</c>, a separator line is rendered before this action.
	/// </summary>
	public bool HasSeparatorBefore { get; init; }
}
