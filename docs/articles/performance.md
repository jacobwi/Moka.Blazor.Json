# Performance

## Virtualized Rendering

The viewer uses Blazor's `Virtualize` component to render only visible nodes. This means documents with millions of nodes render just as fast as small ones &mdash; only the visible viewport is in the DOM.

## Streaming Large Documents

For documents too large to hold in a single string, use the `JsonStream` parameter for incremental parsing:

```html
<MokaJsonViewer JsonStream="@_stream" />

@code {
    private Stream? _stream;

    private async Task LoadLargeFile()
    {
        _stream = File.OpenRead("large-data.json");
    }
}
```

The streaming threshold is configurable:

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.StreamingThresholdBytes = 2 * 1024 * 1024; // 2 MB
});
```

## Background Statistics

For large documents, node count and max depth are computed on a background thread to avoid blocking the UI. The threshold is configurable:

```csharp
options.BackgroundStatsThresholdBytes = 512 * 1024; // 512 KB
```

## Document Size Limits

The maximum accepted document size defaults to 100 MB:

```csharp
options.MaxDocumentSizeBytes = 200 * 1024 * 1024; // 200 MB
```

## Clipboard Size Limits

Copy-to-clipboard is guarded against out-of-memory for very large documents:

```csharp
options.MaxClipboardSizeBytes = 100 * 1024 * 1024; // 100 MB
```

If the document exceeds this limit, the user is prompted to scope to a smaller subtree before copying.

## Zero Dependencies

The library uses only `System.Text.Json` &mdash; no third-party packages. It references the ASP.NET Core shared framework via `FrameworkReference`, so it does not force specific package versions on consuming applications.
