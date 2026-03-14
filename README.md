# Moka.Blazor.Json

A high-performance Blazor JSON viewer and editor component with virtualized rendering, search, theming, and plugin support.

[![NuGet](https://img.shields.io/nuget/v/Moka.Blazor.Json.svg)](https://www.nuget.org/packages/Moka.Blazor.Json)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- **Virtualized rendering** - handles documents up to 100 MB with smooth scrolling
- **Search** - plain text, regex, case-sensitive with match navigation
- **Theming** - light, dark, auto (system preference), or fully custom via CSS variables
- **Context menu** - built-in actions + extensible custom actions with type/property filtering
- **Breadcrumb navigation** - clickable path segments for easy traversal
- **Streaming parsing** - incremental parsing for large documents via `JsonStream`
- **Key sorting** - sort object keys alphabetically (single level or recursive)
- **Node scoping** - zoom into any subtree
- **Zero external dependencies** - built on `System.Text.Json`
- **Multi-target** - supports .NET 9 and .NET 10

## Installation

```bash
dotnet add package Moka.Blazor.Json
```

## Quick Start

### 1. Register services

```csharp
// Program.cs
builder.Services.AddMokaJsonViewer();
```

### 2. Add the component

```razor
@using Moka.Blazor.Json.Components

<MokaJsonViewer Json="@myJson" />

@code {
    private string myJson = """{"name":"Alice","age":30,"tags":["dev","admin"]}""";
}
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Json` | `string?` | `null` | JSON string to display |
| `JsonStream` | `Stream?` | `null` | Stream for incremental parsing (mutually exclusive with `Json`) |
| `Theme` | `MokaJsonTheme` | `Auto` | `Auto`, `Light`, `Dark`, or `Inherit` |
| `ShowToolbar` | `bool` | `true` | Show the toolbar |
| `ShowBottomBar` | `bool` | `true` | Show the status bar |
| `ShowBreadcrumb` | `bool` | `true` | Show the breadcrumb path |
| `ShowLineNumbers` | `bool` | `false` | Show line numbers |
| `ReadOnly` | `bool` | `true` | Disable editing |
| `MaxDepthExpanded` | `int` | `2` | Initial expansion depth |
| `Height` | `string` | `"400px"` | Component height |
| `OnNodeSelected` | `EventCallback<JsonNodeSelectedEventArgs>` | | Fires when a node is clicked |
| `OnJsonChanged` | `EventCallback<JsonChangeEventArgs>` | | Fires when JSON is modified |
| `OnError` | `EventCallback<JsonErrorEventArgs>` | | Fires on parse/runtime errors |
| `ContextMenuActions` | `IReadOnlyList<MokaJsonContextAction>?` | `null` | Custom context menu actions |
| `ToolbarExtra` | `RenderFragment?` | `null` | Extra toolbar content |

## Configuration

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

## Programmatic Control

The component implements `IMokaJsonViewer` for programmatic access:

```razor
<MokaJsonViewer @ref="_viewer" Json="@json" />

@code {
    private MokaJsonViewer _viewer = null!;

    private async Task NavigateToUser()
    {
        await _viewer.NavigateToAsync("/users/0/name");
    }

    private async Task Search()
    {
        var matchCount = await _viewer.SearchAsync("error", new JsonSearchOptions
        {
            CaseSensitive = false,
            UseRegex = true,
            SearchKeys = true,
            SearchValues = true
        });
    }
}
```

**Available methods:**
- `NavigateToAsync(jsonPointer)` - Navigate to a JSON Pointer path
- `ExpandToDepth(depth)` - Expand nodes to depth (-1 for all)
- `ExpandAll()` / `CollapseAll()` - Expand or collapse the entire tree
- `SearchAsync(query, options)` - Search with optional regex/case sensitivity
- `NextMatch()` / `PreviousMatch()` - Navigate search results
- `ClearSearch()` - Clear search state
- `GetJson(indented)` - Get the current JSON as a string

## Custom Context Menu Actions

Add type-aware context menu actions:

```csharp
private readonly List<MokaJsonContextAction> _actions =
[
    new MokaJsonContextAction
    {
        Id = "open-url",
        Label = "Open URL",
        ShortcutHint = "Enter",
        Order = 500,
        HasSeparatorBefore = true,
        IsVisible = ctx => ctx.ValueKind == JsonValueKind.String
                           && ctx.RawValuePreview.StartsWith("\"http", StringComparison.OrdinalIgnoreCase),
        OnExecute = ctx =>
        {
            var url = ctx.RawValuePreview.Trim('"');
            // Open URL...
            return ValueTask.CompletedTask;
        }
    },
    new MokaJsonContextAction
    {
        Id = "format-currency",
        Label = "Format as Currency",
        Order = 510,
        IsVisible = ctx => ctx.ValueKind == JsonValueKind.Number
                           && ctx.PropertyName is "salary" or "price" or "amount",
        OnExecute = ctx =>
        {
            // Format number as currency...
            return ValueTask.CompletedTask;
        }
    }
];
```

```razor
<MokaJsonViewer Json="@json" ContextMenuActions="_actions" />
```

## Theming

### Built-in themes

```razor
<MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Dark" />
```

### Custom CSS variables

Override any of these CSS custom properties:

```css
.my-custom-theme {
    /* Syntax highlighting */
    --moka-json-color-key: #0451a5;
    --moka-json-color-string: #a31515;
    --moka-json-color-number: #098658;
    --moka-json-color-boolean: #0000ff;
    --moka-json-color-null: #808080;
    --moka-json-color-bracket: #319331;

    /* UI */
    --moka-json-bg: transparent;
    --moka-json-color-text: #1e1e1e;
    --moka-json-color-border: #e0e0e0;
    --moka-json-color-hover: rgba(0, 0, 0, 0.04);
    --moka-json-color-selected: rgba(0, 120, 212, 0.1);
    --moka-json-toolbar-bg: #f3f3f3;
    --moka-json-context-bg: #ffffff;

    /* Search highlighting */
    --moka-json-color-search-match: rgba(234, 192, 0, 0.4);
    --moka-json-color-search-active: rgba(234, 128, 0, 0.6);

    /* Typography */
    --moka-json-font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
    --moka-json-font-size: 13px;
    --moka-json-line-height: 1.6;
}
```

Use `Theme="MokaJsonTheme.Inherit"` to apply your own theme class without any built-in overrides.

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+F` | Toggle search |
| `F3` / `Enter` | Next match |
| `Shift+F3` | Previous match |
| `Esc` | Close search |
| `Ctrl+Z` | Undo (edit mode) |
| `Ctrl+Y` | Redo (edit mode) |

## Event Callbacks

```razor
<MokaJsonViewer Json="@json"
                OnNodeSelected="HandleNodeSelected"
                OnError="HandleError" />

@code {
    private void HandleNodeSelected(JsonNodeSelectedEventArgs e)
    {
        Console.WriteLine($"Selected: {e.Path} ({e.ValueKind})");
        Console.WriteLine($"Property: {e.PropertyName}");
        Console.WriteLine($"Preview: {e.RawValuePreview}");
    }

    private void HandleError(JsonErrorEventArgs e)
    {
        Console.WriteLine($"Error at line {e.LineNumber}: {e.Message}");
    }
}
```

## Streaming Large Documents

For documents too large to hold in a string, use `JsonStream`:

```razor
<MokaJsonViewer JsonStream="@_stream" />

@code {
    private Stream? _stream;

    private async Task LoadLargeFile()
    {
        _stream = File.OpenRead("large-data.json");
    }
}
```

## Project Structure

```
src/
  Moka.Blazor.Json/              # Main component library
  Moka.Blazor.Json.Abstractions/ # Interfaces and models
tests/
  Moka.Blazor.Json.Tests/        # Unit + bUnit component tests
  Moka.Blazor.Json.Benchmarks/   # Performance benchmarks
samples/
  Moka.Blazor.Json.Demo/         # Interactive demo application
```

## License

MIT
