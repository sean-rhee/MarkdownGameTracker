(() => {
  "use strict";

  const combobox = document.querySelector("[data-game-title-suggestions]");
  if (!combobox) {
    return;
  }

  const input = combobox.querySelector("input[role='combobox']");
  const list = combobox.querySelector("[role='listbox']");
  const form = combobox.closest("form");
  const status = form?.querySelector("[data-game-title-suggestion-status]");
  const endpoint = combobox.dataset.suggestionsUrl;
  if (!input || !list || !form || !endpoint) {
    return;
  }

  const minimumQueryLength = 2;
  const maximumVisibleSuggestions = 7;
  const debounceDelay = 300;
  let debounceTimer;
  let requestController;
  let suggestions = [];
  let activeIndex = -1;
  let selectedMatch = null;

  const announce = (message) => {
    if (status) {
      status.textContent = message;
    }
  };

  const closeSuggestions = () => {
    list.hidden = true;
    input.setAttribute("aria-expanded", "false");
    input.removeAttribute("aria-activedescendant");
    activeIndex = -1;
  };

  const dismissSuggestions = () => {
    window.clearTimeout(debounceTimer);
    requestController?.abort();
    closeSuggestions();
  };

  const setActiveSuggestion = (index) => {
    activeIndex = index;
    suggestions.forEach(({ element }, suggestionIndex) => {
      const isActive = suggestionIndex === activeIndex;
      element.classList.toggle("active", isActive);
      element.setAttribute("aria-selected", String(isActive));
      if (isActive) {
        input.setAttribute("aria-activedescendant", element.id);
        element.scrollIntoView({ block: "nearest" });
      }
    });
  };

  const chooseSuggestion = (suggestion) => {
    input.value = suggestion.title;
    selectedMatch = suggestion;
    closeSuggestions();
    announce(`Selected ${suggestion.title}.`);
    input.focus();
  };

  const createSuggestion = (match, index) => {
    const element = document.createElement("li");
    element.id = `gameTitleSuggestion-${index}`;
    element.className = "game-title-suggestion";
    element.setAttribute("role", "option");
    element.setAttribute("aria-selected", "false");

    if (match.coverUrl) {
      const cover = document.createElement("img");
      cover.className = "game-title-suggestion-cover";
      cover.src = match.coverUrl;
      cover.alt = "";
      cover.loading = "lazy";
      cover.referrerPolicy = "no-referrer";
      element.append(cover);
    } else {
      const placeholder = document.createElement("span");
      placeholder.className = "game-title-suggestion-cover game-title-suggestion-cover-placeholder";
      placeholder.setAttribute("aria-hidden", "true");
      placeholder.textContent = "GG";
      element.append(placeholder);
    }

    const details = document.createElement("span");
    details.className = "game-title-suggestion-details";
    const title = document.createElement("span");
    title.className = "game-title-suggestion-title";
    title.textContent = match.title;
    details.append(title);

    if (match.releaseYear) {
      const year = document.createElement("span");
      year.className = "game-title-suggestion-year";
      year.textContent = String(match.releaseYear);
      details.append(year);
    }

    element.append(details);
    return element;
  };

  const renderSuggestions = (matches) => {
    list.replaceChildren();
    suggestions = matches.slice(0, maximumVisibleSuggestions).map((match, index) => {
      const element = createSuggestion(match, index);
      list.append(element);
      return {
        element,
        title: match.title,
        igdbGameId: match.igdbGameId
      };
    });

    if (suggestions.length === 0) {
      closeSuggestions();
      announce("No IGDB title suggestions found.");
      return;
    }

    activeIndex = -1;
    list.hidden = false;
    input.setAttribute("aria-expanded", "true");
    announce(`${suggestions.length} IGDB title ${suggestions.length === 1 ? "suggestion" : "suggestions"} available.`);
  };

  const loadSuggestions = async (query) => {
    requestController?.abort();
    requestController = new AbortController();

    try {
      const url = new URL(endpoint, window.location.origin);
      url.searchParams.set("query", query);
      const response = await fetch(url, {
        headers: { Accept: "application/json" },
        signal: requestController.signal
      });
      if (!response.ok) {
        throw new Error(`Suggestion request failed with ${response.status}`);
      }

      const result = await response.json();
      if (input.value.trim() !== query) {
        return;
      }

      if (result.status === "available") {
        renderSuggestions(Array.isArray(result.matches) ? result.matches : []);
      } else {
        closeSuggestions();
        announce(result.status === "notConfigured"
          ? "IGDB suggestions are not configured."
          : "IGDB title suggestions are unavailable.");
      }
    } catch (error) {
      if (error.name !== "AbortError") {
        closeSuggestions();
        announce("IGDB title suggestions are unavailable.");
      }
    }
  };

  const queueSuggestions = () => {
    window.clearTimeout(debounceTimer);
    requestController?.abort();

    const query = input.value.trim();
    if (selectedMatch?.title !== query) {
      selectedMatch = null;
    }

    if (query.length < minimumQueryLength) {
      closeSuggestions();
      announce("");
      return;
    }

    debounceTimer = window.setTimeout(() => loadSuggestions(query), debounceDelay);
  };

  input.addEventListener("input", queueSuggestions);
  input.addEventListener("keydown", (event) => {
    if (list.hidden || suggestions.length === 0) {
      return;
    }

    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActiveSuggestion((activeIndex + 1) % suggestions.length);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveSuggestion(activeIndex <= 0 ? suggestions.length - 1 : activeIndex - 1);
    } else if (event.key === "Enter" && activeIndex >= 0) {
      event.preventDefault();
      chooseSuggestion(suggestions[activeIndex]);
    } else if (event.key === "Escape") {
      event.preventDefault();
      dismissSuggestions();
    }
  });

  list.addEventListener("pointerdown", (event) => {
    const element = event.target.closest("[role='option']");
    const suggestion = suggestions.find((candidate) => candidate.element === element);
    if (!suggestion) {
      return;
    }

    event.preventDefault();
    chooseSuggestion(suggestion);
  });

  document.addEventListener("pointerdown", (event) => {
    if (!combobox.contains(event.target)) {
      dismissSuggestions();
    }
  });

  form.addEventListener("submit", () => {
    const title = input.value.trim();
    if (selectedMatch?.title === title) {
      window.GameGardenIgdbSelection?.set(title, selectedMatch.igdbGameId);
    }
    dismissSuggestions();
  });
})();
