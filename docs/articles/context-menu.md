# Custom Context Menu

The viewer comes with built-in context menu actions (Copy Value, Copy Path, Expand/Collapse, Sort Keys, etc.). You can add custom actions alongside them.

## Adding Custom Actions

```csharp
private readonly List<MokaJsonContextAction> _actions =
[
    new MokaJsonContextAction
    {
        Id = "open-url",
        Label = "Open URL",
        ShortcutHint = "Enter",
        Order = 500,
        HasSeparatorBefore = true,
        IsVisible = ctx => ctx.ValueKind == JsonValueKind.String
                           && ctx.RawValue.StartsWith("\"http", StringComparison.OrdinalIgnoreCase),
        OnExecute = ctx =>
        {
            var url = ctx.RawValue.Trim('"');
            // Open URL in browser...
            return ValueTask.CompletedTask;
        }
    }
];
```

```html
<MokaJsonViewer Json="@json" ContextMenuActions="_actions" />
```

## MokaJsonContextAction Properties

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Unique identifier |
| `Label` | `string` | Display text in the menu |
| `IconCss` | `string?` | CSS class for an icon |
| `ShortcutHint` | `string?` | Keyboard shortcut hint text |
| `Order` | `int` | Sort order (built-in actions use 10-400) |
| `HasSeparatorBefore` | `bool` | Show a separator line before this action |
| `IsVisible` | `Func<MokaJsonNodeContext, bool>?` | Predicate to control visibility |
| `IsEnabled` | `Func<MokaJsonNodeContext, bool>?` | Predicate to control enabled state |
| `OnExecute` | `Func<MokaJsonNodeContext, ValueTask>` | Action to execute when clicked |

## MokaJsonNodeContext

The context object passed to your action callbacks:

| Property | Type | Description |
|----------|------|-------------|
| `Path` | `string` | JSON Pointer path (RFC 6901), e.g. `/users/0/name` |
| `Depth` | `int` | Depth in the tree (root = 0) |
| `ValueKind` | `JsonValueKind` | Type of the value (Object, Array, String, Number, etc.) |
| `PropertyName` | `string?` | Property name if an object member |
| `RawValue` | `string` | Full raw JSON text of the node |
| `RawValuePreview` | `string` | Truncated preview (max 500 chars) for display |
| `Viewer` | `IMokaJsonViewer` | Reference to the viewer for programmatic control |

## Type-Aware Filtering

Use `IsVisible` and `IsEnabled` to show actions only for relevant node types:

```csharp
new MokaJsonContextAction
{
    Id = "format-currency",
    Label = "Format as Currency",
    Order = 510,
    IsVisible = ctx => ctx.ValueKind == JsonValueKind.Number
                       && ctx.PropertyName is "salary" or "price" or "amount",
    OnExecute = ctx =>
    {
        // Format the number value...
        return ValueTask.CompletedTask;
    }
}
```

## Built-in Edit Actions

When `ReadOnly="false"`, these actions are automatically added to the context menu:

- **Edit Value** &mdash; enter inline edit mode for primitive values
- **Rename Key** &mdash; rename the property key (F2)
- **Delete** &mdash; remove the node
- **Add Property** &mdash; add a new property to an object
- **Add Element** &mdash; append an element to an array
