(() => {
  const previewPlaceholder = '<p class="preview-placeholder">Your formatted preview will appear here.</p>';

  document.querySelectorAll("[data-markdown-editor]").forEach((workspace) => {
    const textarea = workspace.querySelector("textarea");
    const preview = workspace.querySelector("[data-markdown-pane='preview']");
    const status = workspace.querySelector("[data-preview-status]");
    const previewUrl = workspace.dataset.previewUrl;
    let previewTimer;
    let previewRequest;

    if (!textarea || !preview || !previewUrl) {
      return;
    }

    const setView = (view) => {
      workspace.dataset.view = view;
      workspace.querySelectorAll("[data-markdown-view]").forEach((button) => {
        button.setAttribute("aria-pressed", String(button.dataset.markdownView === view));
      });

      if (view === "write") {
        textarea.focus();
      } else {
        queuePreview(0);
      }
    };

    const renderPreview = async () => {
      previewRequest?.abort();
      previewRequest = new AbortController();
      status.textContent = "Updating preview…";

      try {
        const response = await fetch(previewUrl, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ markdown: textarea.value }),
          signal: previewRequest.signal
        });

        if (!response.ok) {
          throw new Error(`Preview request failed with ${response.status}.`);
        }

        const html = await response.text();
        preview.innerHTML = html || previewPlaceholder;
        status.textContent = "Preview updated";
      } catch (error) {
        if (error.name !== "AbortError") {
          status.textContent = "Preview unavailable";
          preview.innerHTML = '<p class="preview-error">The preview could not be loaded. Your Markdown is still safe in the editor.</p>';
        }
      }
    };

    function queuePreview(delay = 260) {
      window.clearTimeout(previewTimer);
      previewTimer = window.setTimeout(renderPreview, delay);
    }

    workspace.querySelectorAll("[data-markdown-view]").forEach((button) => {
      button.addEventListener("click", () => setView(button.dataset.markdownView));
    });

    workspace.querySelectorAll("[data-markdown-action]").forEach((button) => {
      button.addEventListener("click", () => applyFormatting(textarea, button.dataset.markdownAction));
    });

    textarea.addEventListener("input", () => queuePreview());
    textarea.addEventListener("keydown", (event) => {
      if (!(event.ctrlKey || event.metaKey)) {
        return;
      }

      const shortcuts = { b: "bold", i: "italic", k: "link" };
      const action = shortcuts[event.key.toLowerCase()];
      if (action) {
        event.preventDefault();
        applyFormatting(textarea, action);
      }
    });

    if (window.matchMedia("(max-width: 767.98px)").matches) {
      setView("write");
    } else {
      setView(workspace.dataset.view || "split");
    }
    queuePreview(0);
  });

  function applyFormatting(textarea, action) {
    const value = textarea.value;
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const selected = value.slice(start, end);

    const replaceSelection = (replacement, selectionStart, selectionEnd) => {
      textarea.setRangeText(replacement, start, end, "end");
      textarea.setSelectionRange(start + selectionStart, start + selectionEnd);
      textarea.focus();
      textarea.dispatchEvent(new Event("input", { bubbles: true }));
    };

    const wrap = (before, after, placeholder) => {
      const content = selected || placeholder;
      const replacement = `${before}${content}${after}`;
      replaceSelection(replacement, before.length, before.length + content.length);
    };

    const prefixLines = (prefixFactory) => {
      const lineStart = value.lastIndexOf("\n", Math.max(0, start - 1)) + 1;
      const nextNewline = value.indexOf("\n", end);
      const lineEnd = nextNewline === -1 ? value.length : nextNewline;
      const lines = value.slice(lineStart, lineEnd).split("\n");
      const replacement = lines.map((line, index) => `${prefixFactory(index)}${line}`).join("\n");
      textarea.setRangeText(replacement, lineStart, lineEnd, "select");
      textarea.focus();
      textarea.dispatchEvent(new Event("input", { bubbles: true }));
    };

    switch (action) {
      case "heading":
        prefixLines(() => "## ");
        break;
      case "bold":
        wrap("**", "**", "bold text");
        break;
      case "italic":
        wrap("_", "_", "italic text");
        break;
      case "quote":
        prefixLines(() => "> ");
        break;
      case "bulleted-list":
        prefixLines(() => "- ");
        break;
      case "numbered-list":
        prefixLines((index) => `${index + 1}. `);
        break;
      case "link":
        wrap("[", "](https://)", "link text");
        break;
      case "code":
        if (selected.includes("\n")) {
          wrap("```\n", "\n```", "code");
        } else {
          wrap("`", "`", "code");
        }
        break;
    }
  }
})();
