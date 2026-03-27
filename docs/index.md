---
title: "Moka.Blazor.Json"
---

# Moka.Blazor.Json

A high-performance Blazor JSON viewer and editor component with virtualized rendering, search, theming, and extensible context menus.

## Get Started

```bash
dotnet add package Moka.Blazor.Json
```

```csharp
// Program.cs
builder.Services.AddMokaJsonViewer();
```

```razor
@using Moka.Blazor.Json.Components

<MokaJsonViewer Json="@myJson" />
```

## Key Features

- **Virtualized rendering** — handles documents up to 2 GB with lazy parsing and smooth scrolling
- **Inline editing** — double-click to edit values, rename keys, add/delete nodes with undo/redo
- **Search** — plain text, regex, case-sensitive with match navigation
- **Theming** — light, dark, auto, or fully custom via CSS variables
- **Context menu** — built-in actions + extensible custom actions with type/property filtering
- **Lazy parsing** — documents over 50 MB use byte-offset indexing with on-demand subtree parsing
- **Streaming search** — search large documents without loading the full DOM
- **AI assistant** — optional AI chat panel for JSON analysis, summarization, and transformation
- **Zero dependencies** — core library built on `System.Text.Json`

## Packages

| Package | Description |
|---------|-------------|
| [Moka.Blazor.Json](https://www.nuget.org/packages/Moka.Blazor.Json) | Main component library |
| [Moka.Blazor.Json.Abstractions](https://www.nuget.org/packages/Moka.Blazor.Json.Abstractions) | Interfaces and models for programmatic access |
| [Moka.Blazor.Json.AI](https://www.nuget.org/packages/Moka.Blazor.Json.AI) | AI assistant panel for JSON analysis (LM Studio, Ollama, ONNX, or custom) |
| [Moka.Blazor.Json.Diagnostics](https://www.nuget.org/packages/Moka.Blazor.Json.Diagnostics) | Debug overlay for lazy parsing diagnostics |

## Next Steps

- [Getting Started](guide/getting-started.md) — installation, setup, and basic usage
- [Configuration](guide/configuration.md) — global options and collapse modes
- [AI Assistant](guide/ai-assistant.md) — set up the AI chat panel
- [API Reference](/api) — full API documentation
