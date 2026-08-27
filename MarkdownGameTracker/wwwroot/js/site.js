(() => {
  const storageKey = "game-garden-theme";
  const root = document.documentElement;
  const toggle = document.querySelector("#themeToggle");
  const themeColor = document.querySelector("#themeColor");
  const preference = window.matchMedia("(prefers-color-scheme: dark)");

  const readStoredTheme = () => {
    try {
      return localStorage.getItem(storageKey);
    } catch {
      return null;
    }
  };

  const applyTheme = (theme) => {
    const isDark = theme === "dark";
    root.dataset.theme = theme;
    root.setAttribute("data-bs-theme", theme);

    if (themeColor) {
      themeColor.content = isDark ? "#0f1e2d" : "#b3d9ff";
    }

    if (toggle) {
      const nextMode = isDark ? "day" : "night";
      toggle.setAttribute("aria-label", `Switch to ${nextMode} mode`);
      toggle.setAttribute("title", `Switch to ${nextMode} mode`);
      const text = toggle.querySelector(".theme-toggle-text");
      if (text) {
        text.textContent = isDark ? "Day mode" : "Night mode";
      }
    }
  };

  applyTheme(root.dataset.theme === "dark" ? "dark" : "light");

  toggle?.addEventListener("click", () => {
    const nextTheme = root.dataset.theme === "dark" ? "light" : "dark";
    try {
      localStorage.setItem(storageKey, nextTheme);
    } catch {
      // The theme still works for this page when storage is unavailable.
    }
    applyTheme(nextTheme);
  });

  preference.addEventListener("change", (event) => {
    if (!readStoredTheme()) {
      applyTheme(event.matches ? "dark" : "light");
    }
  });
})();
