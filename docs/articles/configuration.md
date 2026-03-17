# Configuration

## MokaJsonViewerOptions

Configure global defaults via the options pattern when registering services:

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.DefaultTheme = MokaJsonTheme.Dark;
    options.DefaultExpandDepth = 3;
    options.MaxDocumentSizeBytes = 200 * 1024 * 1024; // 200 MB
    options.SearchDebounceMs = 300;
    options.StreamingThresholdBytes = 2 * 1024 * 1024; // 2 MB
    options.MaxClipboardSizeBytes = 100 * 1024 * 1024; // 100 MB
});
```

### Options Reference

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `DefaultTheme` | `MokaJsonTheme` | `Auto` | Default theme for all viewer instances |
| `DefaultExpandDepth` | `int` | `2` | Default depth to expand on load |
| `MaxDocumentSizeBytes` | `long` | `104857600` (100 MB) | Maximum document size accepted |
| `EnableEditMode` | `bool` | `true` | Whether edit mode is available |
| `SearchDebounceMs` | `int` | `250` | Debounce delay for search input |
| `StreamingThresholdBytes` | `long` | `1048576` (1 MB) | Size threshold for streaming parsing |
| `BackgroundStatsThresholdBytes` | `long` | `524288` (512 KB) | Size threshold for background stats calculation |
| `MaxClipboardSizeBytes` | `long` | `52428800` (50 MB) | Maximum size for clipboard copy |

## Collapse Mode

Control how the tree is initially displayed with the `CollapseMode` parameter:

```cshtml
@* Default: expand to MaxDepthExpanded *@
<MokaJsonViewer Json="@json" CollapseMode="MokaJsonCollapseMode.Depth" MaxDepthExpanded="3" />

@* Show only root brackets { } or [ ] *@
<MokaJsonViewer Json="@json" CollapseMode="MokaJsonCollapseMode.Root" />

@* Expand everything *@
<MokaJsonViewer Json="@json" CollapseMode="MokaJsonCollapseMode.Expanded" />
```

## Toggle Styles and Sizes

Customize the expand/collapse toggle appearance:

```cshtml
@* Different styles *@
<MokaJsonViewer Json="@json" ToggleStyle="MokaJsonToggleStyle.Chevron" />
<MokaJsonViewer Json="@json" ToggleStyle="MokaJsonToggleStyle.PlusMinus" />
<MokaJsonViewer Json="@json" ToggleStyle="MokaJsonToggleStyle.Arrow" />

@* Different sizes *@
<MokaJsonViewer Json="@json" ToggleSize="MokaJsonToggleSize.ExtraSmall" />
<MokaJsonViewer Json="@json" ToggleSize="MokaJsonToggleSize.Medium" />
<MokaJsonViewer Json="@json" ToggleSize="MokaJsonToggleSize.ExtraLarge" />
```

Available styles: `Triangle` (default), `Chevron`, `PlusMinus`, `Arrow`

Available sizes: `ExtraSmall`, `Small` (default), `Medium`, `Large`, `ExtraLarge`
