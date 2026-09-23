(() => {
  const editor = document.querySelector("[data-progress-editor]");
  const textarea = editor?.querySelector("textarea");
  if (!editor || !textarea) return;

  const focusEditor = () => {
    if (editor.open) requestAnimationFrame(() => textarea.focus());
  };
  editor.addEventListener("toggle", focusEditor);
  focusEditor();
})();
