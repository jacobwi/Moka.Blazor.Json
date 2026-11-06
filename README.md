# Moka.Blazor.Json

A JSON viewer and editor for Blazor. Use it for a small API response or a file that's many GB.

[![NuGet](https://img.shields.io/nuget/v/Moka.Blazor.Json?style=flat-square&logo=nuget&color=orange)](https://www.nuget.org/packages/Moka.Blazor.Json)
[![Build](https://img.shields.io/github/actions/workflow/status/jacobwi/Moka.Blazor.Json/ci.yml?style=flat-square&label=build)](https://github.com/jacobwi/Moka.Blazor.Json/actions)
![.NET 9 and 10](https://img.shields.io/badge/.NET-9%20%7C%2010-512BD4?style=flat-square&logo=dotnet)

Works in Blazor Server, WebAssembly and MAUI Hybrid.

## Get started

```bash
dotnet add package Moka.Blazor.Json
```

Register it in `Program.cs`:

```csharp
builder.Services.AddMokaJsonViewer();
```

Add it to a page:

```razor
@using Moka.Blazor.Json.Components

<MokaJsonViewer Json="@json" />

@code {
    private string json = """{"name":"Alice","age":30,"tags":["dev","admin"]}""";
}
```

## Let people edit

Turn off read-only and bind the value. `json` updates as they edit.

```razor
<MokaJsonViewer @bind-Json="json" ReadOnly="false" />
```

Double-click a value to change it, or right-click to add, delete or rename. `Ctrl+Z` and `Ctrl+Y` undo and redo.

## Open large files

Pass a stream instead of a string. Keep it open while the viewer shows it, and close it yourself afterwards.

```razor
<MokaJsonViewer JsonStream="file" Height="600px" />

@code {
    private FileStream file = File.OpenRead("big.json");
}
```

The file is not loaded into memory. The viewer scans it once to build a small index, then reads only the parts you open from disk. This happens automatically for anything over 50 MB.

Here's a 5.6 GB file with 134 million nodes, on a desktop PC with the file already in the Windows file cache:

| | |
|---|---|
| Open the file | 9.5 s, with a progress bar and a Cancel button |
| Memory after opening | 42 MB |
| Memory after expanding and searching | 165 MB |
| Expand an array with 18,151 items | 9 ms |
| Search the whole file | 9 s, with progress |

A few things work differently on documents this size:

- They're read-only.
- Expand All stops after 100,000 rows and tells you. Right-click a node to open or scope to just that part.
- Search stops at 10,000 matches and shows `10,000+`.
- Arrays and objects with more than 100,000 items open as groups, such as `[0 ... 99,999]`.
- Copy and Export say so if the content is over their size limits, rather than running out of memory.

**Uploads:** pass `file.OpenReadStream(long.MaxValue)` from `InputFile`. It can't be read on demand, so it's copied to a temp file first. That file is deleted when the viewer closes it.

**WebAssembly:** the browser has one thread and no real disk, so the temp file sits in the page's memory. The limit there is 1 GB by default.

## What's included

- Search with plain text or regex, and step through the matches
- Light, dark and system themes, or your own with CSS variables
- A right-click menu you can add your own actions to
- Breadcrumb path, line numbers, and sorting keys
- Scope to any node to work with just that part
- A settings panel so people can change the display themselves

## Common parameters

| Parameter | Default | What it does |
|---|---|---|
| `Json` | | The JSON to show, as a string |
| `JsonStream` | | The JSON to show, as a stream |
| `ReadOnly` | `true` | Set to `false` to allow editing |
| `Height` | `400px` | Height of the viewer |
| `Theme` | `Auto` | `Auto`, `Light`, `Dark` or `Inherit` |
| `MaxDepthExpanded` | `2` | How many levels start open |
| `ShowLineNumbers` | `false` | Show line numbers |
| `WordWrap` | `true` | Wrap long values |
| `OnNodeSelected` | | Runs when someone clicks a node |
| `OnJsonChanged` | | Runs when the JSON is edited |
| `OnError` | | Runs when loading fails |

More are listed in the [docs](https://jacobwi.github.io/Moka.Blazor.Json/).

## Settings for every viewer

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.DefaultTheme = MokaJsonTheme.Dark;
    options.DefaultExpandDepth = 3;
    options.LazyParsingThresholdBytes = 50 * 1024 * 1024; // large file mode starts here
    options.MaxClipboardSizeBytes = 50 * 1024 * 1024;
    options.MaxExportSizeBytes = 512 * 1024 * 1024;
    options.MaxSearchMatches = 10_000;
});
```

## Control it from code

```razor
<MokaJsonViewer @ref="viewer" Json="@json" />

@code {
    private MokaJsonViewer viewer = null!;

    private async Task FindErrors()
    {
        await viewer.NavigateToAsync("/users/0/name");
        int matches = await viewer.SearchAsync("error");
    }
}
```

Also available: `ExpandAllAsync()`, `CollapseAll()`, `ExpandToDepth(depth)`, `NextMatchAsync()`, `PreviousMatchAsync()`, `ClearSearch()`, `UndoAsync()`, `RedoAsync()` and `GetJson()`.

## Add to the right-click menu

```razor
<MokaJsonViewer Json="@json" ContextMenuActions="actions" />

@code {
    private readonly List<MokaJsonContextAction> actions =
    [
        new MokaJsonContextAction
        {
            Id = "open-url",
            Label = "Open URL",
            IsVisible = ctx => ctx.ValueKind == JsonValueKind.String
                               && ctx.RawValuePreview.StartsWith("\"http", StringComparison.OrdinalIgnoreCase),
            OnExecute = ctx =>
            {
                string url = ctx.RawValuePreview.Trim('"');
                // open it
                return ValueTask.CompletedTask;
            }
        }
    ];
}
```

## Keyboard shortcuts

| Keys | Action |
|---|---|
| `Ctrl+F` | Search |
| `F3` or `Enter` | Next match |
| `Shift+F3` | Previous match |
| `Esc` | Close search |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `F2` | Rename key |
| `Delete` | Delete node |

## Optional packages

**Moka.Blazor.Json.AI** adds a chat panel that can summarize, explain and transform your JSON. It works with LM Studio, Ollama, ONNX or your own `IChatClient`. It doesn't build for WebAssembly yet.

```bash
dotnet add package Moka.Blazor.Json.AI
```

```razor
<MokaJsonViewer @ref="viewer" Json="@json" />
<MokaJsonAiPanel Viewer="viewer" />
```

Register it with `builder.Services.AddMokaJsonAi()`. See the [AI guide](https://jacobwi.github.io/Moka.Blazor.Json/guide/ai-assistant).

**Moka.Blazor.Json.Diagnostics** adds an overlay showing what large file mode is doing: bytes parsed, cache hits and recent reads.

```bash
dotnet add package Moka.Blazor.Json.Diagnostics
```

```razor
<MokaJsonViewer @ref="viewer" JsonStream="file" />
<MokaJsonDebugOverlay Viewer="viewer" Enabled="true" />
```

**Moka.Blazor.Json.Abstractions** has the interfaces and models on their own, if you only need to talk to a viewer.

## Docs and license

Full docs are at [jacobwi.github.io/Moka.Blazor.Json](https://jacobwi.github.io/Moka.Blazor.Json/).

MIT
