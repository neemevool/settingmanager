export function insertAtCursor(textareaId, text) {
    const ta = document.getElementById(textareaId);
    if (!ta) {
        return null;
    }

    const start = typeof ta.selectionStart === "number" ? ta.selectionStart : ta.value.length;
    const end = typeof ta.selectionEnd === "number" ? ta.selectionEnd : ta.value.length;

    const before = ta.value.substring(0, start);
    const after = ta.value.substring(end);
    ta.value = before + text + after;

    const caret = start + text.length;
    ta.selectionStart = caret;
    ta.selectionEnd = caret;
    ta.focus();

    ta.dispatchEvent(new Event("input", { bubbles: true }));
    ta.dispatchEvent(new Event("change", { bubbles: true }));

    return ta.value;
}
