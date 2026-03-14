using Microsoft.JSInterop;

namespace Moka.Blazor.Json.Interop;

/// <summary>
///     Wraps JS interop calls for the Moka JSON viewer, using JS isolation modules.
/// </summary>
internal sealed class MokaJsonInterop(IJSRuntime jsRuntime) : IAsyncDisposable
{
	#region Fields

	private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() =>
		jsRuntime.InvokeAsync<IJSObjectReference>(
			"import", "./_content/Moka.Blazor.Json/js/moka-json.js").AsTask());

	private bool _disposed;

	#endregion

	#region Public Methods

	/// <summary>
	///     Copies text to the clipboard.
	/// </summary>
	public async ValueTask<bool> CopyToClipboardAsync(string text, CancellationToken cancellationToken = default)
	{
		IJSObjectReference module = await GetModuleAsync().ConfigureAwait(false);
		return await module.InvokeAsync<bool>("copyToClipboard", cancellationToken, text).ConfigureAwait(false);
	}

	/// <summary>
	///     Scrolls an element into view within a container.
	/// </summary>
	public async ValueTask ScrollIntoViewAsync(string containerId, string elementId,
		CancellationToken cancellationToken = default)
	{
		IJSObjectReference module = await GetModuleAsync().ConfigureAwait(false);
		await module.InvokeVoidAsync("scrollIntoView", cancellationToken, containerId, elementId).ConfigureAwait(false);
	}

	/// <summary>
	///     Positions the context menu at the given coordinates.
	/// </summary>
	public async ValueTask PositionContextMenuAsync(string menuId, double clientX, double clientY,
		CancellationToken cancellationToken = default)
	{
		IJSObjectReference module = await GetModuleAsync().ConfigureAwait(false);
		await module.InvokeVoidAsync("positionContextMenu", cancellationToken, menuId, clientX, clientY)
			.ConfigureAwait(false);
	}

	/// <summary>
	///     Adds a listener to dismiss the context menu on outside clicks.
	/// </summary>
	public async ValueTask<int> AddContextMenuDismissListenerAsync<T>(DotNetObjectReference<T> dotNetRef, string menuId,
		CancellationToken cancellationToken = default) where T : class
	{
		IJSObjectReference module = await GetModuleAsync().ConfigureAwait(false);
		return await module.InvokeAsync<int>("addContextMenuDismissListener", cancellationToken, dotNetRef, menuId)
			.ConfigureAwait(false);
	}

	/// <summary>
	///     Removes the context menu dismiss listener.
	/// </summary>
	public async ValueTask RemoveContextMenuDismissListenerAsync(int handlerId,
		CancellationToken cancellationToken = default)
	{
		IJSObjectReference module = await GetModuleAsync().ConfigureAwait(false);
		await module.InvokeVoidAsync("removeContextMenuDismissListener", cancellationToken, handlerId)
			.ConfigureAwait(false);
	}

	/// <summary>
	///     Gets the user's preferred color scheme ('dark' or 'light').
	/// </summary>
	public async ValueTask<string> GetPreferredColorSchemeAsync(CancellationToken cancellationToken = default)
	{
		IJSObjectReference module = await GetModuleAsync().ConfigureAwait(false);
		return await module.InvokeAsync<string>("getPreferredColorScheme", cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	///     Focuses an element by ID.
	/// </summary>
	public async ValueTask FocusElementAsync(string elementId, CancellationToken cancellationToken = default)
	{
		IJSObjectReference module = await GetModuleAsync().ConfigureAwait(false);
		await module.InvokeVoidAsync("focusElement", cancellationToken, elementId).ConfigureAwait(false);
	}

	#endregion

	#region Private Methods

	private async Task<IJSObjectReference> GetModuleAsync()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		return await _moduleTask.Value.ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		if (_moduleTask.IsValueCreated)
		{
			try
			{
				IJSObjectReference module = await _moduleTask.Value.ConfigureAwait(false);
				await module.DisposeAsync().ConfigureAwait(false);
			}
			catch (JSDisconnectedException)
			{
				// Circuit disconnected, safe to ignore
			}
		}
	}

	#endregion
}
