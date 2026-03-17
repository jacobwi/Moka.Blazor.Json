# Getting Started

## Installation

```bash
dotnet add package Moka.Blazor.Json
```

The package targets .NET 9 and .NET 10. It uses `FrameworkReference` so it will not force specific package versions on your project.

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

```cshtml
@using Moka.Blazor.Json.Components

<MokaJsonViewer Json="@myJson" />

@code {
    private string myJson = """{"name":"Alice","age":30,"tags":["dev","admin"]}""";
}
```

## Two-Way Binding

Use `@bind-Json` to keep your variable in sync when the document is edited:

```cshtml
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

```cshtml
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

```cshtml
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

- `NavigateToAsync(jsonPointer)` &mdash; navigate to a JSON Pointer path (RFC 6901)
- `ExpandToDepth(depth)` &mdash; expand nodes to depth (-1 for all)
- `ExpandAll()` / `CollapseAll()` &mdash; expand or collapse the entire tree
- `SearchAsync(query, options)` &mdash; search with optional regex/case sensitivity
- `NextMatch()` / `PreviousMatch()` &mdash; navigate search results
- `ClearSearch()` &mdash; clear search state
- `Undo()` / `Redo()` &mdash; undo/redo edit operations
- `GetJson(indented)` &mdash; get the current JSON as a string

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+F` | Toggle search |
| `F3` / `Enter` | Next match |
| `Shift+F3` | Previous match |
| `Esc` | Close search |
| `Ctrl+Z` | Undo (edit mode) |
| `Ctrl+Y` | Redo (edit mode) |
