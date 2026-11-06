---
title: "Performance"
---

# Performance

## Only the visible rows are rendered

The viewer uses Blazor's `Virtualize` component, so a document with millions of rows has about 60 of them on the page at a time.

## Large files

Pass a seekable stream for anything too big to hold in a string. Keep it open while the viewer shows it, and close it yourself afterwards.

```html
<MokaJsonViewer JsonStream="@_stream" Height="600px" />

@code {
    private Stream? _stream;

    private void Open() => _stream = File.OpenRead("large-data.json");
}
```

Any document over `LazyParsingThresholdBytes` (50 MB, or 16 MB in WebAssembly) opens in large file mode:

1. The file is read once, in blocks, to build an index. Each block is thrown away after it's scanned, so memory doesn't grow with the file.
2. Containers of 4 MB or more are indexed. Everything smaller is parsed from disk when someone opens it, which takes a few milliseconds.
3. Parsed parts go in a cache with a size limit.

The file is never loaded whole, so its size isn't limited by memory or by the 2 GB limit on arrays. On a 5.6 GB file with 134 million nodes the index held 61 containers and used 7 MB.

### What's different in large file mode

- The document is read-only.
- The first view opens at most 2,000 rows, and leaves containers with more than 1,000 children closed.
- Expand All stops after 100,000 rows, nearest the root first, and tells the user. It can be cancelled.
- Search scans the bytes directly and shows progress. It stops at `MaxSearchMatches` (10,000) and shows `10,000+`.
- A value over 1 GB can't be loaded whole. Copy and Export refuse anything over `MaxClipboardSizeBytes` and `MaxExportSizeBytes` before they try.

### Very long containers

Any container with more than 100,000 children opens as groups, such as `[0 ... 99,999]`. Opening it costs a few rows, and only the groups someone opens are read. It also keeps the list within the height a browser can scroll, which is about 1.5 million rows. This applies to documents of every size.

Rows show the first 10,000 characters of a very long value and say how many more there are. Copy Value still gets all of it.

### Streams that can't seek

Browser uploads and network responses can't be read on demand. They're copied first: to memory while they're under the threshold, then to a temp file that's deleted when the viewer closes it. `MaxDocumentSizeBytes` is checked as they're copied.

```csharp
private async Task OnUpload(InputFileChangeEventArgs e) =>
    _stream = e.File.OpenReadStream(long.MaxValue);
```

## Editing

Each edit re-parses the document, and undo history keeps a full copy of it per step, up to 50 steps or 256 MB in total.

Values over 16,000 characters can't be edited inline. On Blazor Server each keystroke sends the whole value, and a message over the default 32 KB limit ends the circuit.

## WebAssembly

The browser gives Blazor one thread, so `Task.Run` doesn't take work off the UI. Indexing and streaming search stop between blocks to let the page paint. Parsing a single part can't be split, so a large in-memory document still pauses the page while it parses, which is why the threshold is lower here.

There's no real disk either. A temp file sits in the page's memory, so `MaxDocumentSizeBytes` defaults to 1 GB in WebAssembly.

Ahead-of-time compilation makes parsing much faster, at the cost of a bigger download:

```xml
<PropertyGroup>
  <RunAOTCompilation>true</RunAOTCompilation>
</PropertyGroup>
```

The library is marked as trimmable.

## Prerendering

During prerendering, a stream or a large string isn't loaded. The viewer shows its loading state and loads once it's interactive. A stream that can't seek could only be read once, and the work would hold up the page's first response.

## Limits

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.LazyParsingThresholdBytes = 50 * 1024 * 1024;
    options.MaxDocumentSizeBytes = 100L * 1024 * 1024 * 1024;
    options.MaxClipboardSizeBytes = 50 * 1024 * 1024;
    options.MaxExportSizeBytes = 512 * 1024 * 1024;
    options.MaxSearchMatches = 10_000;
});
```

## Dependencies

The library is built on `System.Text.Json` and has no third-party packages. It references `Microsoft.AspNetCore.Components.Web`, which lets it run in WebAssembly and MAUI as well as on the server.
