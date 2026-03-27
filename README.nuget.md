# Moka.Blazor.Json

A high-performance Blazor JSON viewer and editor component with virtualized rendering, lazy parsing, search, theming, and extensible context menus.

## Features

- **Virtualized rendering** - smooth scrolling for documents up to 2 GB
- **Lazy parsing** - byte-offset indexing for documents over 50 MB
- **Inline editing** - double-click to edit values, rename keys, add/delete nodes, undo/redo
- **Search** - plain text, regex, case-sensitive with match navigation
- **Theming** - light, dark, auto, or custom via CSS variables
- **Settings panel** - built-in gear menu for runtime configuration
- **Toolbar modes** - text, icon, or both
- **Context menu** - built-in + custom actions with type/property filtering
- **Zero dependencies** - built on System.Text.Json
- **Multi-target** - .NET 9 and .NET 10

## Quick Start

Register services in `Program.cs`:

```csharp
builder.Services.AddMokaJsonViewer();
```

Add the component:

```razor
@using Moka.Blazor.Json.Components

<MokaJsonViewer Json="@myJson" />

@code {
    private string myJson = """{"name":"Alice","age":30}""";
}
```

## Configuration

```csharp
builder.Services.AddMokaJsonViewer(options =>
{
    options.DefaultTheme = MokaJsonTheme.Dark;
    options.DefaultToolbarMode = MokaJsonToolbarMode.IconAndText;
    options.DefaultExpandDepth = 3;
    options.LazyParsingThresholdBytes = 50 * 1024 * 1024;
    options.ShowSettingsButton = true;
});
```

## Key Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| Json | string? | null | JSON string to display |
| Theme | MokaJsonTheme | Auto | Auto, Light, Dark, or Inherit |
| ReadOnly | bool | true | Disable editing |
| MaxDepthExpanded | int | 2 | Initial expansion depth |
| ToolbarMode | MokaJsonToolbarMode? | null | Text, Icon, or IconAndText |
| ToggleStyle | MokaJsonToggleStyle | Triangle | Triangle, Chevron, PlusMinus, Arrow |
| ShowSettingsButton | bool? | null | Show settings gear in toolbar |
| WordWrap | bool | true | Enable word wrapping |
| Height | string | 400px | Component height |

## Related Packages

| Package | Description |
|---------|-------------|
| Moka.Blazor.Json | Main component library |
| Moka.Blazor.Json.Abstractions | Interfaces and models |
| Moka.Blazor.Json.AI | AI assistant panel for JSON analysis (LM Studio, Ollama, ONNX) |
| Moka.Blazor.Json.Diagnostics | Debug overlay for lazy parsing |

## Documentation

Full docs: https://jacobwi.github.io/Moka.Blazor.Json/

## License

MIT
