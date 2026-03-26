---
title: "Diagnostics"
---

# Diagnostics

The `Moka.Blazor.Json.Diagnostics` package provides a debug overlay for monitoring lazy parsing performance in real time. It's useful during development to understand how the viewer interacts with large documents.

## Installation

```bash
dotnet add package Moka.Blazor.Json.Diagnostics
```

## Usage

Add the `MokaJsonDebugOverlay` component alongside your viewer. Pass a `@ref` to the viewer so the overlay can read its stats:

```razor
@using Moka.Blazor.Json.Components
@using Moka.Blazor.Json.Diagnostics.Components

<MokaJsonViewer @ref="_viewer" Json="@json" />

<MokaJsonDebugOverlay Viewer="_viewer" Enabled="showDiagnostics" />

@code {
    private MokaJsonViewer? _viewer;
    private bool showDiagnostics = true;
    private string json = """{"hello": "world"}""";
}
```

## What It Shows

The overlay displays real-time statistics organized into sections:

### Coverage

A visual progress bar showing how much of the document has been parsed on demand:

- **Unique bytes parsed** — actual bytes touched (no double-counting)
- **Total bytes** — full document size
- **Coverage %** — percentage of document explored

### Parsing

- **Subtree parses** — number of on-demand parse operations triggered by expanding nodes
- **Cumulative I/O** — total bytes processed across all parses (may exceed document size due to re-parses)
- **Parse time** — average and maximum duration per subtree parse

### Cache

The lazy parser uses an LRU cache to avoid re-parsing recently viewed subtrees:

- **Hits / Misses** — cache lookup results
- **Hit rate** — percentage of cache hits (color-coded: green >= 80%, yellow >= 50%, red < 50%)

### Index

- **Entries** — number of nodes in the byte-offset structural index
- **Lazy expansions** — number of times child nodes were indexed on demand

### Recent Parses

A rolling list of the last 6 parse operations, showing:

- **Path** — JSON Pointer path of the parsed subtree
- **Size** — bytes parsed
- **Duration** — parse time

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Viewer` | `MokaJsonViewer?` | `null` | The viewer instance to read stats from |
| `Enabled` | `bool` | `true` | Whether the overlay is visible |

## When to Use

- **Development only** — the diagnostics package adds a visual overlay; don't ship it to production
- **Large document testing** — use it when testing with documents over 50 MB to verify lazy parsing is working efficiently
- **Cache tuning** — monitor hit rates to understand if the LRU cache size is appropriate
- **Performance profiling** — identify which subtrees are expensive to parse

## Notes

- The overlay only shows data when the viewer is in **lazy mode** (documents above the `LazyParsingThresholdBytes` threshold)
- For small documents parsed with `JsonDocument`, the stats will be `null` and the overlay won't render
- Toggle visibility at runtime by binding the `Enabled` parameter to a checkbox or button
