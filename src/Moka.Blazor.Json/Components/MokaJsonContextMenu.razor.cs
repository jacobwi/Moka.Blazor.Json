using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moka.Blazor.Json.Interop;
using Moka.Blazor.Json.Models;

namespace Moka.Blazor.Json.Components;

/// <summary>
///     Context menu component with customizable actions per node.
///     Positioned entirely via C# inline styles to avoid Blazor/JS DOM conflicts.
/// </summary>
public sealed partial class MokaJsonContextMenu : ComponentBase, IAsyncDisposable
{
    #region Injected Services

    [Inject] private MokaJsonInterop Interop { get; set; } = null!;

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await RemoveDismissListenerAsync();
        _selfRef?.Dispose();
    }

    #endregion

    #region Parameters

    /// <summary>The list of available context actions.</summary>
    [Parameter]
    public IReadOnlyList<MokaJsonContextAction>? Actions { get; set; }

    /// <summary>Whether the context menu is currently visible.</summary>
    [Parameter]
    public bool IsVisible { get; set; }

    /// <summary>The context of the node on which the menu was invoked.</summary>
    [Parameter]
    public MokaJsonNodeContext? NodeContext { get; set; }

    /// <summary>Callback when the context menu should be dismissed.</summary>
    [Parameter]
    public EventCallback OnDismiss { get; set; }

    #endregion

    #region State Fields

    private string MenuId { get; } = $"moka-ctx-{Guid.NewGuid():N}";
    private double _left;
    private double _top;
    private int _dismissListenerId;
    private DotNetObjectReference<MokaJsonContextMenu>? _selfRef;

    #endregion

    #region Computed Properties

    private string MenuStyle => string.Create(CultureInfo.InvariantCulture,
        $"left:{_left}px;top:{_top}px;display:block;");

    private IEnumerable<MokaJsonContextAction> VisibleActions =>
        (Actions ?? [])
        .Where(a => a.IsVisible?.Invoke(NodeContext!) ?? true)
        .OrderBy(a => a.Order);

    #endregion

    #region Public API

    /// <summary>
    ///     Shows the context menu at the given viewport coordinates.
    /// </summary>
    public async Task ShowAsync(double clientX, double clientY)
    {
        // Clean up previous dismiss listener if any
        await RemoveDismissListenerAsync();

        _left = clientX;
        _top = clientY;
        StateHasChanged();

        // Register a dismiss listener for clicks outside
        _selfRef ??= DotNetObjectReference.Create(this);
        _dismissListenerId = await Interop.AddContextMenuDismissListenerAsync(_selfRef, MenuId);
    }

    /// <summary>
    ///     Called from JS when a click outside the menu occurs.
    /// </summary>
    [JSInvokable]
    public async Task DismissContextMenu()
    {
        await RemoveDismissListenerAsync();
        await OnDismiss.InvokeAsync();
    }

    #endregion

    #region Private Methods

    private static string GetItemClass(bool isEnabled)
    {
        return isEnabled ? "moka-json-context-item" : "moka-json-context-item moka-json-context-item--disabled";
    }

    private async Task HandleAction(MokaJsonContextAction action, bool isEnabled)
    {
        if (!isEnabled || NodeContext is null) return;
        await action.OnExecute(NodeContext);
        await RemoveDismissListenerAsync();
        await OnDismiss.InvokeAsync();
    }

    private async Task RemoveDismissListenerAsync()
    {
        if (_dismissListenerId != 0)
        {
            try
            {
                await Interop.RemoveContextMenuDismissListenerAsync(_dismissListenerId);
            }
            catch (JSDisconnectedException)
            {
            }

            _dismissListenerId = 0;
        }
    }

    #endregion
}