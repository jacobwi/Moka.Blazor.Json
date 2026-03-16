# API Reference

## Packages

### Moka.Blazor.Json

The main component library containing the `MokaJsonViewer` Blazor component, supporting services, models, and event types.

Key namespaces:

- `Moka.Blazor.Json.Components` &mdash; `MokaJsonViewer` and related Razor components
- `Moka.Blazor.Json.Models` &mdash; enums, event args, options, and context action types
- `Moka.Blazor.Json.Extensions` &mdash; `AddMokaJsonViewer()` service registration

### Moka.Blazor.Json.Abstractions

Interfaces and search options for programmatic control of the viewer.

- `IMokaJsonViewer` &mdash; programmatic API for navigation, search, expand/collapse, undo/redo
- `JsonSearchOptions` &mdash; search configuration (case sensitivity, regex, key/value targeting)
