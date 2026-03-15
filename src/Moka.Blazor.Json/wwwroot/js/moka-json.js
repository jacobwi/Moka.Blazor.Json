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
 * Scrolls an element into view within a container.
 * @param {string} containerId - The container element ID.
 * @param {string} elementId - The target element ID to scroll into view.
 */
export function scrollIntoView(containerId, elementId) {
    const container = document.getElementById(containerId);
    const element = document.getElementById(elementId);
    if (container && element) {
        element.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
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

    if (!window.__mokaJsonSchemeHandler) window.__mokaJsonSchemeHandler = [];
    window.__mokaJsonSchemeHandler.push({ mq, handler });
}

/**
 * Removes the color scheme listener.
 */
export function removeColorSchemeListener() {
    if (!window.__mokaJsonSchemeHandler) return;
    for (const { mq, handler } of window.__mokaJsonSchemeHandler) {
        mq.removeEventListener('change', handler);
    }
    window.__mokaJsonSchemeHandler = [];
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
