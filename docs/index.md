---
_layout: landing
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

```html
@using Moka.Blazor.Json.Components

<MokaJsonViewer Json="@myJson" />
```

## Key Features

- **Virtualized rendering** &mdash; handles documents up to 100 MB with smooth scrolling
- **Inline editing** &mdash; double-click to edit values, rename keys, add/delete nodes with undo/redo
- **Search** &mdash; plain text, regex, case-sensitive with match navigation
- **Theming** &mdash; light, dark, auto, or fully custom via CSS variables
- **Context menu** &mdash; built-in actions + extensible custom actions with type/property filtering
- **Streaming** &mdash; incremental parsing for large documents via `JsonStream`
- **Zero dependencies** &mdash; built on `System.Text.Json`

## Packages

| Package | Description |
|---------|-------------|
| [Moka.Blazor.Json](https://www.nuget.org/packages/Moka.Blazor.Json) | Main component library |
| [Moka.Blazor.Json.Abstractions](https://www.nuget.org/packages/Moka.Blazor.Json.Abstractions) | Interfaces and models for programmatic access |

## Next Steps

- [Getting Started](articles/getting-started.md) &mdash; installation, setup, and basic usage
- [API Reference](api/index.md) &mdash; full API documentation
