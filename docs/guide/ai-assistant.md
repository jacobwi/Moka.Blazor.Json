---
title: "AI Assistant"
---

# AI Assistant

The `Moka.Blazor.Json.AI` package adds an AI-powered chat panel that understands your JSON data. Ask questions, summarize documents, analyze structure, generate schemas, transform data, and run queries — all with streaming responses and selection-aware context.

## Installation

```bash
dotnet add package Moka.Blazor.Json.AI
```

## Setup

### 1. Register services

```csharp
// Program.cs
builder.Services.AddMokaJsonViewer();
builder.Services.AddMokaJsonAi(); // Default: LM Studio at localhost:1234
```

### 2. Add the components

```razor
@using Moka.Blazor.Json.Components
@using Moka.Blazor.Json.AI.Components

<MokaJsonViewer @ref="_viewer" Json="@myJson" />
<MokaJsonAiPanel Viewer="_viewer" />

@code {
    private MokaJsonViewer _viewer = null!;
    private string myJson = """{"users":[{"name":"Alice","age":30}]}""";
}
```

## Providers

### OpenAI-compatible (default)

Works with LM Studio, vLLM, or any OpenAI API-compatible server:

```csharp
builder.Services.AddMokaJsonAi(options =>
{
    options.Endpoint = "http://localhost:1234/v1"; // default
    options.DefaultModel = "local-model";
});
```

### Ollama

```csharp
builder.Services.AddMokaJsonAi(options =>
{
    options.Provider = AiProvider.Ollama;
    options.Endpoint = "http://localhost:11434"; // default
    options.DefaultModel = "llama3.2";
});
```

### ONNX Runtime GenAI (embedded)

Run AI models fully in-process with no external server. Requires the companion package:

```bash
dotnet add package Moka.Blazor.AI.Onnx
```

```csharp
using Moka.Blazor.AI.Onnx.Extensions;

builder.Services.AddMokaJsonAi(); // Register JSON AI services
builder.Services.AddMokaAiOnnx(@"C:\models\phi-3.5-mini-instruct-onnx");
```

Hardware acceleration variants:
- `Microsoft.ML.OnnxRuntimeGenAI` — CPU
- `Microsoft.ML.OnnxRuntimeGenAI.Cuda` — NVIDIA CUDA
- `Microsoft.ML.OnnxRuntimeGenAI.DirectML` — DirectML (Windows)

### Custom IChatClient

Bring your own `Microsoft.Extensions.AI.IChatClient`:

```csharp
builder.Services.AddMokaJsonAi(myCustomChatClient);
```

## Quick Actions

The panel includes configurable quick action buttons:

| Action | Description |
|--------|-------------|
| **Summarize** | Plain-language summary of the JSON document |
| **Analyze** | Structure analysis, patterns, and data quality insights |
| **Schema** | Generate a JSON Schema from the document |
| **Transform** | Suggest data transformations |
| **Query** | Help build queries for the data (JSONPath, jq, LINQ) |
| **Selection** | Analyze the currently selected node/subtree |

## Chat Styles

Three visual styles are available, switchable from the settings gear:

| Style | Description |
|-------|-------------|
| **Bubble** | Modern chat bubbles with avatars, gradient backgrounds, rounded corners |
| **Classic** | Clean flat layout, left-aligned messages, subtle dividers |
| **Compact** | Minimal spacing, no avatars — optimized for small panels |

Set the default via the `ChatStyle` parameter:

```razor
<MokaJsonAiPanel Viewer="_viewer" ChatStyle="ChatStyle.Classic" />
```

## Settings

Click the gear icon to access runtime settings:

- **Model** — switch the active model name
- **Chat Style** — Bubble, Classic, or Compact
- **Temperature** — 0.0 (precise) to 2.0 (creative)
- **Max Context** — characters of JSON context sent to the model (500–100,000)
- **Stream Responses** — toggle streaming vs. batch responses

## Context Menu Integration

Add an "Ask AI" option to the JSON viewer's right-click context menu:

```razor
<MokaJsonViewer @ref="_viewer" Json="@json" ContextMenuActions="_contextActions" />
<MokaJsonAiPanel @ref="_aiPanel" Viewer="_viewer" />

@code {
    private MokaJsonViewer _viewer = null!;
    private MokaJsonAiPanel _aiPanel = null!;
    private IReadOnlyList<MokaJsonContextAction>? _contextActions;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && _aiPanel is not null)
        {
            _contextActions = [_aiPanel.CreateAskAiContextAction()];
            StateHasChanged();
        }
    }
}
```

Right-clicking any node shows an **"Ask AI"** action at the bottom of the context menu. Clicking it sends the node's path and data to the AI for analysis.

You can also call `AskAboutNode` programmatically:

```csharp
await _aiPanel.AskAboutNode("/users/0/name");
```

## Features

- **Streaming** — responses appear token-by-token with a typing indicator
- **Stop/cancel** — stop generation mid-stream
- **Edit & re-send** — click the pencil icon on any user message to edit and re-send from that point
- **Copy** — copy assistant responses to clipboard
- **Markdown** — assistant responses render full markdown (code blocks, tables, lists, etc.)
- **Selection context** — the "Selection" button sends the currently selected JSON subtree
- **Token & timing stats** — estimated tokens and response time shown per message
- **Dark mode** — inherits theme from the JSON viewer

## Parameters

### MokaJsonAiPanel

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Viewer` | `MokaJsonViewer?` | `null` | The JSON viewer instance to connect |
| `Title` | `string` | `"JSON AI Assistant"` | Panel header title |
| `Placeholder` | `string` | `"Ask about your JSON..."` | Input placeholder text |
| `MessagesHeight` | `string` | `"350px"` | Max height of the messages area |
| `ShowQuickActions` | `bool` | `true` | Show quick action buttons |
| `ChatStyle` | `ChatStyle` | `Bubble` | Visual style for chat messages |
| `ThemeAttribute` | `string` | `""` | Theme (`"light"`, `"dark"`, `""`) |

### Public Methods

| Method | Description |
|--------|-------------|
| `AskAboutNode(string path)` | Send an AI prompt analyzing the node at the given JSON Pointer path (auto-scopes context to the subtree) |
| `CreateAskAiContextAction(...)` | Returns a pre-built `MokaJsonContextAction` for the viewer's context menu |
| `ScopeToNode(string path)` | Persistently scope the AI context to a specific node's subtree |
| `AddSource(string label, string json)` | Add a named JSON data source for multi-source context (AI sees all sources labeled) |
| `RemoveSource(string label)` | Remove a named data source from the multi-source scope |
| `ClearScope()` | Clear all scopes — AI reverts to seeing the full document |
| `IsSending` | Whether the AI is currently processing a response |

## Scoped Context

You can scope the AI to specific data instead of the full document. This is useful for large JSON files or when comparing multiple objects.

### Single node scope

```csharp
// Focus AI on a specific subtree
_aiPanel.ScopeToNode("/users/0");

// Ask about it — context only includes that node
await _aiPanel.AskAboutNode("/users/0");

// Return to full document context
_aiPanel.ClearScope();
```

### Multi-source scope

Scope the AI to multiple JSON objects simultaneously for comparison or cross-analysis:

```razor
<MokaJsonViewer @ref="_viewer1" Json="@_customerJson" />
<MokaJsonViewer @ref="_viewer2" Json="@_orderJson" />
<MokaJsonAiPanel @ref="_aiPanel" Viewer="_viewer1" />

@code {
    private void ScopeToBoth()
    {
        _aiPanel.AddSource("Customer", _customerJson);
        _aiPanel.AddSource("Order", _orderJson);
    }
}
```

When multiple sources are added, the AI receives them labeled by name:

```
[2 data sources provided]

--- Customer ---
{ "id": "cust_482", "name": "Sarah Chen", ... }

--- Order ---
{ "orderId": "ORD-2026-1847", "customerId": "cust_482", ... }
```
