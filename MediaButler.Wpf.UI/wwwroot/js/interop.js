// Small interop helpers the Blazor components need that have no built-in
// equivalent: focusing an element by id, scrolling the log pane to bottom,
// and a focus-trap for the accessible modal dialog (Tab/Shift+Tab cycle
// within the dialog, Escape closes it, focus returns to the invoking
// control on close). See docs/BIBLE.md MediaButler.Wpf.UI entry.
window.mediaButlerInterop = (function () {
    let lastFocused = null;
    let trapHandler = null;

    function focusById(id) {
        const el = document.getElementById(id);
        if (el) el.focus();
    }

    function scrollToBottom(id) {
        const el = document.getElementById(id);
        if (el) el.scrollTop = el.scrollHeight;
    }

    function getFocusable(container) {
        return Array.from(container.querySelectorAll(
            'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        )).filter(el => el.offsetParent !== null);
    }

    function openDialog(dialogId, dotNetRef) {
        lastFocused = document.activeElement;
        const dialog = document.getElementById(dialogId);
        if (!dialog) return;

        const focusable = getFocusable(dialog);
        (focusable[0] || dialog).focus();

        trapHandler = function (e) {
            if (e.key === 'Escape') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnEscape');
                return;
            }
            if (e.key !== 'Tab') return;
            const items = getFocusable(dialog);
            if (items.length === 0) return;
            const first = items[0];
            const last = items[items.length - 1];
            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        };
        document.addEventListener('keydown', trapHandler, true);
    }

    function closeDialog() {
        if (trapHandler) {
            document.removeEventListener('keydown', trapHandler, true);
            trapHandler = null;
        }
        if (lastFocused && typeof lastFocused.focus === 'function') {
            lastFocused.focus();
        }
        lastFocused = null;
    }

    return { focusById, scrollToBottom, openDialog, closeDialog };
})();
