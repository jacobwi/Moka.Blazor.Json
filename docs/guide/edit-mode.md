---
title: "Edit Mode"
---

# Edit Mode

Set `ReadOnly="false"` to enable inline editing of JSON documents.

## Basic Setup

```html
<MokaJsonViewer @bind-Json="myJson" ReadOnly="false" />
```

## Inline Value Editing

**Double-click** any primitive value (string, number, boolean, null) to enter inline edit mode:

- **Strings** and **numbers**: a text input appears for direct editing
- **Booleans**: a dropdown with `true`/`false` options
- Press **Enter** to commit, **Escape** to cancel
- Click outside the input to commit

Values are validated before committing:

- Numbers must be valid numeric values
- Booleans must be `true` or `false`
- A `null` value can become anything: type a number, `true`/`false`, `null`, a quoted string, an object or an array. Input that isn't valid JSON is stored as a string, so `hello` becomes `"hello"`

## Key Renaming

Right-click a property and select **Rename Key** (or select it and press **F2**) to rename an object key. Duplicate keys are rejected.

## Adding Nodes

Via the context menu:

- **Add Property** (on objects): adds a new `"newProperty": null` entry
- **Add Element** (on arrays): appends a `null` element

Double-click the new `null` to give it a value.

## Deleting Nodes

Right-click any node and select **Delete** (or select it and press **Delete**) to remove it from the parent object or array.

## Undo / Redo

Edit mode maintains a snapshot-based undo/redo history (up to 50 entries):

- **Ctrl+Z**: undo the last edit
- **Ctrl+Y** or **Ctrl+Shift+Z**: redo the last undone edit

The history is cleared when the viewer loads a different document.

All mutations (value edits, key renames, add, delete, sort) are tracked.

## Change Events

Two event callbacks report edit changes:

```html
<MokaJsonViewer Json="@json"
                ReadOnly="false"
                OnJsonChanged="HandleChange"
                JsonChanged="json => currentJson = json" />

@code {
    private void HandleChange(JsonChangeEventArgs e)
    {
        Console.WriteLine($"Change: {e.ChangeType} at {e.Path}");
        Console.WriteLine($"Old: {e.OldValue}");
        Console.WriteLine($"New: {e.NewValue}");
    }
}
```

`JsonChangeType` values: `ValueChanged`, `NodeAdded`, `NodeRemoved`, `KeyRenamed`, `KeysSorted`