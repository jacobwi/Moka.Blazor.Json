# Moka.Blazor.Json

A JSON viewer and editor for Blazor. Use it for a small API response or a file that's many GB.

Works in Blazor Server, WebAssembly and MAUI Hybrid, on .NET 9 and 10.

## Get started

Register it in `Program.cs`:

```csharp
builder.Services.AddMokaJsonViewer();
```

Add it to a page:

```razor
@using Moka.Blazor.Json.Components

<MokaJsonViewer Json="@json" />

@code {
    private string json = """{"name":"Alice","age":30}""";
}
```

To allow editing, use `<MokaJsonViewer @bind-Json="json" ReadOnly="false" />`.

## Large files

Pass a stream instead of a string, and keep it open while the viewer shows it:

```razor
<MokaJsonViewer JsonStream="file" Height="600px" />

@code {
    private FileStream file = File.OpenRead("big.json");
}
```

The file is not loaded into memory. The viewer scans it once to build a small index, then reads only the parts you open from disk. A 5.6 GB file opened in 9.5 seconds and used 42 MB. Documents over 50 MB work this way and are read-only.

## What's included

- Search with plain text or regex
- Inline editing with undo and redo
- Light, dark and system themes, or your own with CSS variables
- A right-click menu you can add your own actions to
- Breadcrumb path, line numbers, and sorting keys

## Related packages

| Package | What it adds |
|---|---|
| Moka.Blazor.Json.AI | A chat panel for your JSON (LM Studio, Ollama, ONNX or your own client). Doesn't build for WebAssembly yet |
| Moka.Blazor.Json.Diagnostics | An overlay showing what large file mode is doing |
| Moka.Blazor.Json.Abstractions | The interfaces and models on their own |

## Docs

https://jacobwi.github.io/Moka.Blazor.Json/

## License

MIT
