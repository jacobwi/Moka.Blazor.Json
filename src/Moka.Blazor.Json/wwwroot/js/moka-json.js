/**
 * Moka.Blazor.Json - JS Isolation Module
 * Handles clipboard, scroll, context menu positioning, and DOM measurements.
 */

/**
 * Copies text to the clipboard.
 * @param {string} text
 * @returns {Promise<boolean>}
 */
export async function copyToClipboard(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        // Fallback for older browsers
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        try {
            document.execCommand('copy');
            return true;
        } catch {
            return false;
        } finally {
            document.body.removeChild(textarea);
        }
    }
}

/**
 * Scrolls a row of the virtualized tree into view, centering it when it is off screen.
 * The row may not be in the DOM yet: Virtualize only renders rows near the scroll position,
 * so jump to an estimated offset first and wait for Virtualize to render the target.
 * @param {string} viewportId - The tree viewport element ID.
 * @param {number} rowIndex - Index of the row in the flattened node list.
 */
export function scrollToRow(viewportId, rowIndex) {
    const viewport = document.getElementById(viewportId);
    if (!viewport) return;

    const token = (viewport.__mokaScrollToken || 0) + 1;
    viewport.__mokaScrollToken = token;
    const selector = `#moka-node-${rowIndex}`;

    const reveal = (row) => {
        const vr = viewport.getBoundingClientRect();
        const rr = row.getBoundingClientRect();
        if (rr.top < vr.top || rr.bottom > vr.bottom) {
            viewport.scrollTop += (rr.top - vr.top) - (viewport.clientHeight - rr.height) / 2;
        }
    };

    const existing = viewport.querySelector(selector);
    if (existing) {
        reveal(existing);
        return;
    }

    const rows = viewport.querySelectorAll('.moka-json-node');
    let rowHeight = 20;
    if (rows.length > 1) {
        const first = rows[0].getBoundingClientRect();
        const last = rows[rows.length - 1].getBoundingClientRect();
        rowHeight = Math.max(1, (last.bottom - first.top) / rows.length);
    } else if (rows.length === 1) {
        rowHeight = Math.max(1, rows[0].getBoundingClientRect().height);
    }

    viewport.scrollTop = Math.max(0, rowIndex * rowHeight - viewport.clientHeight / 2);

    let frames = 0;
    const poll = () => {
        if (viewport.__mokaScrollToken !== token || !viewport.isConnected) return;
        const row = viewport.querySelector(selector);
        if (row) {
            reveal(row);
            return;
        }
        if (++frames < 120) requestAnimationFrame(poll);
    };
    requestAnimationFrame(poll);
}

/**
 * Stops browser defaults for the viewer's keyboard shortcuts. Blazor cannot call
 * preventDefault conditionally per key, so this runs alongside the .NET handlers.
 * @param {HTMLElement} root - The viewer root element.
 */
export function attachKeyGuards(root) {
    if (!root || root.__mokaKeyGuards) return;
    root.__mokaKeyGuards = true;
    root.addEventListener('keydown', (e) => {
        const key = e.key;
        if ((e.ctrlKey || e.metaKey) && !e.altKey && (key === 'f' || key === 'F')) {
            e.preventDefault();
            return;
        }

        const searchOpen = root.querySelector('.moka-json-search') !== null;
        if (key === 'F3' && searchOpen) {
            e.preventDefault();
            return;
        }

        const inSearchInput = e.target instanceof HTMLInputElement && e.target.closest('.moka-json-search');
        if (inSearchInput && (key === 'Enter' || key === 'ArrowUp' || key === 'ArrowDown')) {
            e.preventDefault();
        }
    });
}

/**
 * Gets the bounding rectangle of an element.
 * @param {HTMLElement} element
 * @returns {{ x: number, y: number, width: number, height: number } | null}
 */
export function getBoundingRect(element) {
    if (!element) return null;
    const rect = element.getBoundingClientRect();
    return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
}

/**
 * Positions a context menu near the mouse event, adjusting to stay within viewport.
 * @param {string} menuId - The menu element ID.
 * @param {number} clientX - Mouse X coordinate.
 * @param {number} clientY - Mouse Y coordinate.
 */
export function positionContextMenu(menuId, clientX, clientY) {
    const menu = document.getElementById(menuId);
    if (!menu) return;

    menu.style.left = `${clientX}px`;
    menu.style.top = `${clientY}px`;
    menu.style.display = 'block';

    // Adjust if overflowing viewport
    const rect = menu.getBoundingClientRect();
    const vw = window.innerWidth;
    const vh = window.innerHeight;

    if (rect.right > vw) {
        menu.style.left = `${Math.max(0, clientX - rect.width)}px`;
    }
    if (rect.bottom > vh) {
        menu.style.top = `${Math.max(0, clientY - rect.height)}px`;
    }
}

/**
 * Adds a global click listener to close context menu.
 * @param {DotNetObjectReference} dotNetRef
 * @param {string} menuId
 * @returns {number} listener ID for cleanup
 */
export function addContextMenuDismissListener(dotNetRef, menuId) {
    const handler = (e) => {
        const menu = document.getElementById(menuId);
        if (menu && !menu.contains(e.target)) {
            dotNetRef.invokeMethodAsync('DismissContextMenu');
        }
    };
    document.addEventListener('click', handler);
    document.addEventListener('contextmenu', handler);

    // Store for cleanup using small incrementing counter (must fit in Int32)
    if (!window.__mokaJsonHandlers) {
        window.__mokaJsonHandlers = new Map();
        window.__mokaJsonNextId = 1;
    }
    const id = window.__mokaJsonNextId++;
    window.__mokaJsonHandlers.set(id, handler);
    return id;
}

/**
 * Removes the dismiss listener.
 * @param {number} handlerId
 */
export function removeContextMenuDismissListener(handlerId) {
    if (!window.__mokaJsonHandlers) return;
    const handler = window.__mokaJsonHandlers.get(handlerId);
    if (handler) {
        document.removeEventListener('click', handler);
        document.removeEventListener('contextmenu', handler);
        window.__mokaJsonHandlers.delete(handlerId);
    }
}

/**
 * Detects the user's preferred color scheme.
 * @returns {string} 'dark' or 'light'
 */
export function getPreferredColorScheme() {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/**
 * Registers a listener for color scheme changes.
 * @param {DotNetObjectReference} dotNetRef
 */
export function addColorSchemeListener(dotNetRef) {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = (e) => {
        dotNetRef.invokeMethodAsync('OnColorSchemeChanged', e.matches ? 'dark' : 'light');
    };
    mq.addEventListener('change', handler);

    // Store for cleanup using shared incrementing counter (must fit in Int32)
    if (!window.__mokaJsonHandlers) {
        window.__mokaJsonHandlers = new Map();
        window.__mokaJsonNextId = 1;
    }
    const id = window.__mokaJsonNextId++;
    window.__mokaJsonHandlers.set(id, { mq, handler });
    return id;
}

/**
 * Removes a specific color scheme listener by ID.
 * @param {number} handlerId
 */
export function removeColorSchemeListener(handlerId) {
    if (!window.__mokaJsonHandlers) return;
    const entry = window.__mokaJsonHandlers.get(handlerId);
    if (entry) {
        entry.mq.removeEventListener('change', entry.handler);
        window.__mokaJsonHandlers.delete(handlerId);
    }
}

/**
 * Focuses an element by ID.
 * @param {string} elementId
 */
export function focusElement(elementId) {
    const el = document.getElementById(elementId);
    if (el) el.focus();
}

/**
 * Focuses an input element and selects all its text.
 * @param {string} elementId
 */
export function focusAndSelectInput(elementId) {
    // Use requestAnimationFrame to ensure the element is rendered
    requestAnimationFrame(() => {
        const el = document.getElementById(elementId);
        if (el) {
            el.focus();
            if (el.select) el.select();
        }
    });
}

/**
 * Downloads a string as a file via the browser.
 * @param {string} fileName - The file name for the download.
 * @param {string} content - The file content.
 */
export function downloadFile(fileName, content) {
    const blob = new Blob([content], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
