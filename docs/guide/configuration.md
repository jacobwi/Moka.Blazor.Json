---
title: "Configuration"
---

# Configuration

## MokaJsonViewerOptions

Configure global defaults via the options pattern when registering services:

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.DefaultTheme = MokaJsonTheme.Dark;
    options.DefaultToolbarMode = MokaJsonToolbarMode.IconAndText;
    options.DefaultExpandDepth = 3;
    options.MaxDocumentSizeBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    options.SearchDebounceMs = 300;
    options.LazyParsingThresholdBytes = 50 * 1024 * 1024; // 50 MB
    options.MaxClipboardSizeBytes = 100 * 1024 * 1024; // 100 MB
    options.ShowSettingsButton = true;
});
```

### Options Reference

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `DefaultTheme` | `MokaJsonTheme` | `Auto` | Default theme for all viewer instances |
| `DefaultToolbarMode` | `MokaJsonToolbarMode` | `IconAndText` | Default toolbar display mode (Text, Icon, or IconAndText) |
| `DefaultExpandDepth` | `int` | `2` | Default depth to expand on load |
| `MaxDocumentSizeBytes` | `long` | 2 GB | Maximum document size accepted |
| `EnableEditMode` | `bool` | `true` | Whether edit mode is available |
| `SearchDebounceMs` | `int` | `250` | Debounce delay for search input |
| `LazyParsingThresholdBytes` | `long` | 50 MB | Size threshold for lazy/indexed parsing |
| `BackgroundStatsThresholdBytes` | `long` | 10 MB | Size threshold for background stats calculation |
| `MaxClipboardSizeBytes` | `long` | 50 MB | Maximum size for clipboard copy |
| `ShowSettingsButton` | `bool` | `true` | Whether the settings gear icon is shown in the toolbar |

## Settings Panel

The viewer includes a built-in settings panel accessible via the gear icon in the toolbar. Users can change settings at runtime without reloading:

- **Display**: Theme, toolbar mode, toggle style, toggle size
- **Layout**: Line numbers, word wrap, breadcrumb, bottom bar
- **Behavior**: Expand depth, collapse mode, read-only toggle
- **Search**: Default case-sensitive and regex modes

To hide the settings button globally:

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.ShowSettingsButton = false;
});
```

Or per-instance:

```html
<MokaJsonViewer Json="@json" ShowSettingsButton="false" />
```

## Toolbar Mode

Control how toolbar buttons are displayed:

```html
@* Text labels only *@
<MokaJsonViewer Json="@json" ToolbarMode="MokaJsonToolbarMode.Text" />

@* Icons only *@
<MokaJsonViewer Json="@json" ToolbarMode="MokaJsonToolbarMode.Icon" />

@* Both icons and text *@
<MokaJsonViewer Json="@json" ToolbarMode="MokaJsonToolbarMode.IconAndText" />
```

## Collapse Mode

Control how the tree is initially displayed with the `CollapseMode` parameter:

```html
@* Default: expand to MaxDepthExpanded *@
<MokaJsonViewer Json="@json" CollapseMode="MokaJsonCollapseMode.Depth" MaxDepthExpanded="3" />

@* Show only root brackets { } or [ ] *@
<MokaJsonViewer Json="@json" CollapseMode="MokaJsonCollapseMode.Root" />

@* Expand everything *@
<MokaJsonViewer Json="@json" CollapseMode="MokaJsonCollapseMode.Expanded" />
```

## Toggle Styles and Sizes

Customize the expand/collapse toggle appearance:

```html
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