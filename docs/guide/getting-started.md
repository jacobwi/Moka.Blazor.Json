---
title: "Getting Started"
---

# Getting Started

## Installation

```bash
dotnet add package Moka.Blazor.Json
```

The package targets .NET 9 and .NET 10, and works in Blazor Server, WebAssembly and MAUI Hybrid.

## Register Services

In your `Program.cs`:

```csharp
builder.Services.AddMokaJsonViewer();
```

Or with custom options:

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.DefaultTheme = MokaJsonTheme.Dark;
    options.DefaultExpandDepth = 3;
});
```

## Basic Usage

```html
@using Moka.Blazor.Json.Components

<MokaJsonViewer Json="@myJson" />

@code {
    private string myJson = """{"name":"Alice","age":30,"tags":["dev","admin"]}""";
}
```

## Two-Way Binding

Use `@bind-Json` to keep your variable in sync when the document is edited:

```html
<MokaJsonViewer @bind-Json="myJson" ReadOnly="false" />

@code {
    private string myJson = """{"name":"Alice"}""";
    // myJson updates automatically when the user edits values
}
```

## Component Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Json` | `string?` | `null` | JSON string to display |
| `JsonStream` | `Stream?` | `null` | Stream for incremental parsing |
| `Theme` | `MokaJsonTheme` | `Auto` | `Auto`, `Light`, `Dark`, or `Inherit` |
| `ShowToolbar` | `bool` | `true` | Show the toolbar |
| `ShowBottomBar` | `bool` | `true` | Show the status bar |
| `ShowBreadcrumb` | `bool` | `true` | Show the breadcrumb path |
| `ShowLineNumbers` | `bool` | `false` | Show line numbers |
| `ReadOnly` | `bool` | `true` | Disable editing |
| `MaxDepthExpanded` | `int` | `2` | Initial expansion depth |
| `CollapseMode` | `MokaJsonCollapseMode` | `Depth` | Initial collapse behavior (`Depth`, `Root`, `Expanded`) |
| `Height` | `string` | `"400px"` | Component height |
| `ToggleStyle` | `MokaJsonToggleStyle` | `Triangle` | Toggle icon style |
| `WordWrap` | `bool` | `true` | Wrap long values to the next line |
| `ToggleSize` | `MokaJsonToggleSize` | `Small` | Toggle icon size |
| `ContextMenuActions` | `IReadOnlyList<MokaJsonContextAction>?` | `null` | Custom context menu actions |
| `ToolbarExtra` | `RenderFragment?` | `null` | Extra toolbar content |

## Event Callbacks

| Event | Type | Description |
|-------|------|-------------|
| `OnNodeSelected` | `EventCallback<JsonNodeSelectedEventArgs>` | Fires when a node is clicked |
| `OnJsonChanged` | `EventCallback<JsonChangeEventArgs>` | Fires when JSON is modified (detailed change info) |
| `JsonChanged` | `EventCallback<string?>` | Two-way binding callback |
| `OnError` | `EventCallback<JsonErrorEventArgs>` | Fires on parse/runtime errors |

```html
<MokaJsonViewer Json="@json"
                OnNodeSelected="HandleNodeSelected"
                OnError="HandleError" />

@code {
    private void HandleNodeSelected(JsonNodeSelectedEventArgs e)
    {
        Console.WriteLine($"Selected: {e.Path} ({e.ValueKind})");
        Console.WriteLine($"Full value: {e.RawValue}");
    }

    private void HandleError(JsonErrorEventArgs e)
    {
        Console.WriteLine($"Error at line {e.LineNumber}: {e.Message}");
    }
}
```

## Programmatic Control

Access the component via `@ref` to call methods on the `IMokaJsonViewer` interface:

```html
<MokaJsonViewer @ref="_viewer" Json="@json" />

@code {
    private MokaJsonViewer _viewer = null!;

    private async Task NavigateToUser()
    {
        await _viewer.NavigateToAsync("/users/0/name");
    }

    private async Task Search()
    {
        int matchCount = await _viewer.SearchAsync("error", new JsonSearchOptions
        {
            CaseSensitive = false,
            UseRegex = true
        });
    }
}
```

**Available methods:**

- `NavigateToAsync(jsonPointer)`: select a node by JSON Pointer (RFC 6901), expanding and scrolling to it. `""` is the root; `"/"` is a property named `""`
- `ExpandToDepth(depth)`: expand nodes to depth (-1 for all)
- `ExpandAllAsync()` / `CollapseAll()`: expand or collapse the entire tree
- `SearchAsync(query, options)`: search with optional regex/case sensitivity, highlighting and scrolling to the first match
- `NextMatchAsync()` / `PreviousMatchAsync()`: move between search results
- `ClearSearch()`: clear search state
- `UndoAsync()` / `RedoAsync()`: undo/redo edit operations
- `GetJson(indented)`: get the current JSON as a string

`ExpandAll()`, `NextMatch()`, `PreviousMatch()`, `Undo()` and `Redo()` still exist. They start the operation without waiting for it and report failures through `OnError`. Prefer the `Async` versions when you need to know it finished.

The async methods are safe to call from any thread; they run on the component's dispatcher.

Settings the user changes in the settings panel (theme, read-only, line numbers and so on) stay in effect when your page re-renders. Passing a *different* value for that parameter takes over again.

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+F` | Open search, or focus the search box if it's already open |
| `Enter` / `F3` / `Down` | Next match (scrolls it into view) |
| `Shift+Enter` / `Shift+F3` / `Up` | Previous match |
| `Esc` | Close search |
| `F2` | Rename the selected key (edit mode) |
| `Delete` | Delete the selected node (edit mode) |
| `Ctrl+Z` | Undo (edit mode) |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo (edit mode) |

Shortcuts apply while focus is inside the viewer; click a node first. `Cmd` works in place of `Ctrl` on macOS.