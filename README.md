# Moka.Blazor.Json

A high-performance Blazor JSON viewer and editor component with virtualized rendering, lazy parsing for large documents, search, theming, and extensible context menus.

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-9.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 9/10" /></a>
  <a href="https://github.com/jacobwi/Moka.Blazor.Json/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" alt="MIT License" /></a>
  <a href="https://github.com/jacobwi/Moka.Blazor.Json/actions"><img src="https://img.shields.io/github/actions/workflow/status/jacobwi/Moka.Blazor.Json/ci.yml?style=flat-square&label=build" alt="Build" /></a>
  <a href="https://www.nuget.org/packages/Moka.Blazor.Json"><img src="https://img.shields.io/nuget/v/Moka.Blazor.Json?style=flat-square&logo=nuget&color=orange" alt="NuGet" /></a>
</p>

## Features

- **Virtualized rendering** — handles documents up to 2 GB with smooth scrolling
- **Lazy parsing** — documents over 50 MB use byte-offset indexing with on-demand subtree parsing
- **Inline editing** — double-click to edit values, rename keys, add/delete nodes with undo/redo
- **Search** — plain text, regex, case-sensitive with match navigation; streaming search for large docs
- **Theming** — light, dark, auto (system preference), or fully custom via CSS variables
- **Settings panel** — built-in gear menu for runtime configuration of all display/behavior settings
- **Toolbar modes** — text-only, icon-only, or icon+text toolbar display
- **Context menu** — built-in actions + extensible custom actions with type/property filtering
- **Breadcrumb navigation** — clickable path segments for easy traversal
- **Key sorting** — sort object keys alphabetically (single level or recursive)
- **Node scoping** — zoom into any subtree as if it were the root
- **AI assistant** — optional AI-powered chat panel for JSON analysis, summarization, and transformation
- **Zero external dependencies** — core library built on `System.Text.Json`
- **Multi-target** — supports .NET 9 and .NET 10

## Packages

| Package | Description |
|---------|-------------|
| [Moka.Blazor.Json](https://www.nuget.org/packages/Moka.Blazor.Json) | Main component library |
| [Moka.Blazor.Json.Abstractions](https://www.nuget.org/packages/Moka.Blazor.Json.Abstractions) | Interfaces and models for programmatic access |
| [Moka.Blazor.Json.AI](https://www.nuget.org/packages/Moka.Blazor.Json.AI) | AI assistant panel for JSON analysis (LM Studio, Ollama, ONNX, or custom) |
| [Moka.Blazor.Json.Diagnostics](https://www.nuget.org/packages/Moka.Blazor.Json.Diagnostics) | Debug overlay for lazy parsing diagnostics |

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

### 3. Two-way binding

```razor
<MokaJsonViewer @bind-Json="myJson" ReadOnly="false" />

@code {
    private string myJson = """{"name":"Alice"}""";
    // myJson updates automatically when the document is modified
}
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Json` | `string?` | `null` | JSON string to display |
| `JsonStream` | `Stream?` | `null` | Stream for large documents |
| `Theme` | `MokaJsonTheme` | `Auto` | `Auto`, `Light`, `Dark`, or `Inherit` |
| `ShowToolbar` | `bool` | `true` | Show the toolbar |
| `ShowBottomBar` | `bool` | `true` | Show the status bar |
| `ShowBreadcrumb` | `bool` | `true` | Show the breadcrumb path |
| `ShowLineNumbers` | `bool` | `false` | Show line numbers |
| `ShowSettingsButton` | `bool?` | `null` | Show settings gear in toolbar (defaults from DI options) |
| `ReadOnly` | `bool` | `true` | Disable editing |
| `MaxDepthExpanded` | `int` | `2` | Initial expansion depth |
| `Height` | `string` | `"400px"` | Component height |
| `ToolbarMode` | `MokaJsonToolbarMode?` | `null` | `Text`, `Icon`, or `IconAndText` |
| `ToggleStyle` | `MokaJsonToggleStyle` | `Triangle` | `Triangle`, `Chevron`, `PlusMinus`, `Arrow` |
| `ToggleSize` | `MokaJsonToggleSize` | `Small` | `ExtraSmall` through `ExtraLarge` |
| `CollapseMode` | `MokaJsonCollapseMode` | `Depth` | `Depth`, `Root`, or `Expanded` |
| `WordWrap` | `bool` | `true` | Enable word wrapping |
| `OnNodeSelected` | `EventCallback<JsonNodeSelectedEventArgs>` | | Fires when a node is clicked |
| `OnJsonChanged` | `EventCallback<JsonChangeEventArgs>` | | Fires when JSON is modified |
| `JsonChanged` | `EventCallback<string?>` | | Two-way binding callback |
| `OnError` | `EventCallback<JsonErrorEventArgs>` | | Fires on parse/runtime errors |
| `ContextMenuActions` | `IReadOnlyList<MokaJsonContextAction>?` | `null` | Custom context menu actions |
| `ToolbarExtra` | `RenderFragment?` | `null` | Extra toolbar content |

## Configuration

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.DefaultTheme = MokaJsonTheme.Dark;
    options.DefaultToolbarMode = MokaJsonToolbarMode.IconAndText;
    options.DefaultExpandDepth = 3;
    options.MaxDocumentSizeBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    options.LazyParsingThresholdBytes = 50 * 1024 * 1024;   // 50 MB
    options.MaxClipboardSizeBytes = 100 * 1024 * 1024;      // 100 MB
    options.SearchDebounceMs = 300;
    options.ShowSettingsButton = true;
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
- `NavigateToAsync(jsonPointer)` — Navigate to a JSON Pointer path
- `ExpandToDepth(depth)` — Expand nodes to depth (-1 for all)
- `ExpandAll()` / `CollapseAll()` — Expand or collapse the entire tree
- `SearchAsync(query, options)` — Search with optional regex/case sensitivity
- `NextMatch()` / `PreviousMatch()` — Navigate search results
- `ClearSearch()` — Clear search state
- `GetJson(indented)` — Get the current JSON as a string

## Custom Context Menu Actions

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
    }
];
```

```razor
<MokaJsonViewer Json="@json" ContextMenuActions="_actions" />
```

## Theming

```razor
@* Built-in themes *@
<MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Dark" />
<MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Light" />
<MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Auto" />

@* Custom theme: use Inherit + your own CSS variables *@
<MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Inherit" />
```

Override CSS custom properties for full control — see [Theming documentation](https://jacobwi.github.io/Moka.Blazor.Json/guide/theming).

## AI Assistant

Add an AI-powered chat panel that understands your JSON data — summarize, analyze structure, generate schemas, transform, and query:

```bash
dotnet add package Moka.Blazor.Json.AI
```

```csharp
// Program.cs
builder.Services.AddMokaJsonViewer();
builder.Services.AddMokaJsonAi(options =>
{
    // Default: LM Studio at localhost:1234
    // For Ollama:
    // options.Provider = AiProvider.Ollama;
    // options.DefaultModel = "llama3.2";
});
```

```razor
@using Moka.Blazor.Json.Components
@using Moka.Blazor.Json.AI.Components

<MokaJsonViewer @ref="_viewer" Json="@myJson" />
<MokaJsonAiPanel Viewer="_viewer" />

@code {
    private MokaJsonViewer _viewer = null!;
    private string myJson = """{"users":[{"name":"Alice","age":30}]}""";
}
```

**Supported providers:**
- **LM Studio / OpenAI-compatible** — any OpenAI API-compatible server (default)
- **Ollama** — native Ollama API support
- **ONNX Runtime GenAI** — fully embedded, no server needed (via `Moka.Blazor.AI.Onnx`)
- **Custom** — bring your own `IChatClient` implementation

**Features:** streaming responses, editable messages, configurable quick actions (Summarize, Analyze, Schema, Transform, Query), selection-aware context, **right-click "Ask AI" context menu action**, adjustable temperature/model/context size, three chat styles (Bubble, Classic, Compact), light/dark theme support.

## Diagnostics

For development, add the diagnostics package to monitor lazy parsing performance:

```bash
dotnet add package Moka.Blazor.Json.Diagnostics
```

```razor
@using Moka.Blazor.Json.Diagnostics.Components

<MokaJsonViewer @ref="_viewer" Json="@json" />
<MokaJsonDebugOverlay Viewer="_viewer" Enabled="true" />
```

The overlay shows real-time coverage, parse stats, cache hit rates, and recent parse operations.

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+F` | Toggle search |
| `F3` / `Enter` | Next match |
| `Shift+F3` | Previous match |
| `Esc` | Close search |
| `Ctrl+Z` | Undo (edit mode) |
| `Ctrl+Y` | Redo (edit mode) |

## Documentation

Full documentation: [jacobwi.github.io/Moka.Blazor.Json](https://jacobwi.github.io/Moka.Blazor.Json/)

## Project Structure

```
src/
  Moka.Blazor.Json/              # Main component library
  Moka.Blazor.Json.Abstractions/ # Interfaces and models
  Moka.Blazor.Json.AI/           # AI assistant panel (optional)
  Moka.Blazor.Json.Diagnostics/  # Debug overlay for lazy parsing
tests/
  Moka.Blazor.Json.Tests/        # Unit + bUnit component tests
  Moka.Blazor.Json.Benchmarks/   # Performance benchmarks
samples/
  Moka.Blazor.Json.Demo/         # Interactive demo application
```

## License

MIT
