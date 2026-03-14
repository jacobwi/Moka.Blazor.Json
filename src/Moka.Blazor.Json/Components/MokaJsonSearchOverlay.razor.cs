using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Search/find panel with text input, match navigation, and mode toggles.
/// </summary>
public sealed partial class MokaJsonSearchOverlay : ComponentBase
{
	#region Parameters

	/// <summary>The current search query.</summary>
	[Parameter]
	public string? Query { get; set; }

	/// <summary>Total number of matches found.</summary>
	[Parameter]
	public int MatchCount { get; set; }

	/// <summary>Zero-based index of the active match.</summary>
	[Parameter]
	public int ActiveMatchIndex { get; set; }

	/// <summary>Whether case-sensitive search is active.</summary>
	[Parameter]
	public bool CaseSensitive { get; set; }

	/// <summary>Whether regex mode is active.</summary>
	[Parameter]
	public bool UseRegex { get; set; }

	/// <summary>Callback when the query text changes.</summary>
	[Parameter]
	public EventCallback<string> OnQueryChanged { get; set; }

	/// <summary>Callback to navigate to the next match.</summary>
	[Parameter]
	public EventCallback OnNext { get; set; }

	/// <summary>Callback to navigate to the previous match.</summary>
	[Parameter]
	public EventCallback OnPrevious { get; set; }

	/// <summary>Callback to close the search overlay.</summary>
	[Parameter]
	public EventCallback OnClose { get; set; }

	/// <summary>Callback when case sensitivity is toggled.</summary>
	[Parameter]
	public EventCallback<bool> OnCaseSensitiveChanged { get; set; }

	/// <summary>Callback when regex mode is toggled.</summary>
	[Parameter]
	public EventCallback<bool> OnRegexChanged { get; set; }

	private string InputId { get; } = $"moka-search-{Guid.NewGuid():N}";

	#endregion

	#region Event Handlers

	private async Task HandleInput(ChangeEventArgs e) => await OnQueryChanged.InvokeAsync(e.Value?.ToString() ?? "");

	private async Task HandleKeyDown(KeyboardEventArgs e)
	{
		switch (e.Key)
		{
			case "Enter" when e.ShiftKey:
			case "F3" when e.ShiftKey:
				await OnPrevious.InvokeAsync();
				break;
			case "Enter":
			case "F3":
				await OnNext.InvokeAsync();
				break;
			case "Escape":
				await OnClose.InvokeAsync();
				break;
		}
	}

	private async Task HandleCaseSensitiveChange(ChangeEventArgs e) =>
		await OnCaseSensitiveChanged.InvokeAsync(bool.TryParse(e.Value?.ToString(), out bool v) && v);

	private async Task HandleRegexChange(ChangeEventArgs e) =>
		await OnRegexChanged.InvokeAsync(bool.TryParse(e.Value?.ToString(), out bool v) && v);

	#endregion
}
