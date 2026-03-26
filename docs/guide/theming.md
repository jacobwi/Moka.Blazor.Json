---
title: "Theming"
---

# Theming

## Built-in Themes

```html
<MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Light" />
<MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Dark" />
<MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Auto" />  @* follows system preference *@
```

| Theme | Description |
|-------|-------------|
| `Auto` | Follows the system's light/dark preference (default) |
| `Light` | Light background with dark text |
| `Dark` | Dark background with light text |
| `Inherit` | No built-in styles applied; bring your own via CSS variables |

## Custom CSS Variables

Override any of these CSS custom properties to create your own theme:

```css
.my-custom-viewer {
    /* Syntax highlighting */
    --moka-json-color-key: #0451a5;
    --moka-json-color-string: #a31515;
    --moka-json-color-number: #098658;
    --moka-json-color-boolean: #0000ff;
    --moka-json-color-null: #808080;
    --moka-json-color-bracket: #319331;

    /* UI chrome */
    --moka-json-bg: transparent;
    --moka-json-color-text: #1e1e1e;
    --moka-json-color-border: #e0e0e0;
    --moka-json-color-hover: rgba(0, 0, 0, 0.04);
    --moka-json-color-selected: rgba(0, 120, 212, 0.1);
    --moka-json-toolbar-bg: #f3f3f3;
    --moka-json-context-bg: #ffffff;

    /* Search highlighting */
    --moka-json-color-search-match: rgba(234, 192, 0, 0.4);
    --moka-json-color-search-active: rgba(234, 128, 0, 0.6);

    /* Typography */
    --moka-json-font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
    --moka-json-font-size: 13px;
    --moka-json-line-height: 1.6;
}
```

Use `Theme="MokaJsonTheme.Inherit"` and wrap the viewer with your custom class:

```html
<div class="my-custom-viewer">
    <MokaJsonViewer Json="@json" Theme="MokaJsonTheme.Inherit" />
</div>
```