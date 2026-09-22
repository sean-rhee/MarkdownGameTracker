(() => {
  const combobox = document.querySelector("[data-search-suggestions]");
  if (!combobox) {
    return;
  }

  const input = combobox.querySelector("input[role='combobox']");
  const list = combobox.querySelector("[role='listbox']");
  const form = combobox.closest("form");
  const results = document.querySelector("[data-search-results]");
  const resultCount = document.querySelector("[data-search-result-count]");
  const emptyState = document.querySelector("[data-search-empty-state]");
  const clearLink = form?.querySelector("[data-search-clear]");
  if (!input || !list || !form || !results || !resultCount || !emptyState || !clearLink) {
    return;
  }

  // Match .NET invariant casing without expanding one character into several
  // (for example, JavaScript normally uppercases German sharp s to "SS").
  const normalizeSearch = value => Array.from(value.trim(), character => {
    const uppercase = character.toUpperCase();
    return Array.from(uppercase).length === 1 ? uppercase : character;
  }).join("");
  const maximumVisibleSuggestions = 7;
  const cards = Array.from(results.querySelectorAll("[data-game-card]"))
    .map((element) => ({
      element,
      normalizedSearchText: (element.dataset.searchText ?? "")
    }));
  const statusLinks = Array.from(document.querySelectorAll(".status-tab"));
  const dependentSearchInputs = Array.from(
    document.querySelectorAll("input[type='hidden'][name='search']")
  );
  const suggestions = Array.from(list.querySelectorAll("[data-search-suggestion]"))
    .map((element, index) => ({
      element,
      index,
      value: element.dataset.searchValue ?? element.textContent.trim(),
      normalizedValue: normalizeSearch(element.dataset.searchValue ?? element.textContent)
    }));
  let visibleSuggestions = [];
  let activeIndex = -1;

  suggestions.forEach((suggestion) => {
    suggestion.element.id = `searchSuggestion-${suggestion.index}`;
  });

  const closeSuggestions = () => {
    list.hidden = true;
    input.setAttribute("aria-expanded", "false");
    input.removeAttribute("aria-activedescendant");
    activeIndex = -1;
    suggestions.forEach(({ element }) => {
      element.hidden = true;
      element.classList.remove("active");
      element.setAttribute("aria-selected", "false");
    });
  };

  const setActiveSuggestion = (index) => {
    activeIndex = index;
    visibleSuggestions.forEach(({ element }, suggestionIndex) => {
      const isActive = suggestionIndex === activeIndex;
      element.classList.toggle("active", isActive);
      element.setAttribute("aria-selected", String(isActive));
      if (isActive) {
        input.setAttribute("aria-activedescendant", element.id);
        element.scrollIntoView({ block: "nearest" });
      }
    });
  };

  const updateSuggestions = () => {
    const query = normalizeSearch(input.value);
    if (!query) {
      closeSuggestions();
      return;
    }

    visibleSuggestions = suggestions
      .filter(({ normalizedValue }) => normalizedValue.includes(query) && normalizedValue !== query)
      .sort((left, right) => {
        const leftStartsWithQuery = left.normalizedValue.startsWith(query);
        const rightStartsWithQuery = right.normalizedValue.startsWith(query);
        if (leftStartsWithQuery !== rightStartsWithQuery) {
          return leftStartsWithQuery ? -1 : 1;
        }
        return left.value.localeCompare(right.value);
      })
      .slice(0, maximumVisibleSuggestions);

    const visibleElements = new Set(visibleSuggestions.map(({ element }) => element));
    suggestions.forEach(({ element }) => {
      element.hidden = !visibleElements.has(element);
      element.classList.remove("active");
      element.setAttribute("aria-selected", "false");
    });

    if (visibleSuggestions.length === 0) {
      closeSuggestions();
      return;
    }

    activeIndex = -1;
    input.removeAttribute("aria-activedescendant");
    list.hidden = false;
    input.setAttribute("aria-expanded", "true");
  };

  const setSearchParameter = (url, query) => {
    url.searchParams.delete("Search");
    if (query) {
      url.searchParams.set("search", query);
    } else {
      url.searchParams.delete("search");
    }
  };

  const updateResults = (syncBrowserUrl = true) => {
    const query = input.value.trim();
    const normalizedQuery = normalizeSearch(query);
    let visibleCount = 0;

    cards.forEach(({ element, normalizedSearchText }) => {
      const isVisible = !normalizedQuery || normalizedSearchText.includes(normalizedQuery);
      element.hidden = !isVisible;
      if (isVisible) {
        visibleCount += 1;
      }
    });

    resultCount.textContent = `${visibleCount} ${visibleCount === 1 ? "result" : "results"}`;
    results.hidden = visibleCount === 0;
    emptyState.hidden = visibleCount !== 0;
    clearLink.hidden = !query;

    dependentSearchInputs.forEach((searchInput) => {
      searchInput.value = query;
    });

    statusLinks.forEach((link) => {
      const url = new URL(link.href, window.location.href);
      setSearchParameter(url, query);
      link.href = `${url.pathname}${url.search}${url.hash}`;
    });

    if (syncBrowserUrl) {
      const url = new URL(window.location.href);
      setSearchParameter(url, query);
      window.history.replaceState(
        window.history.state,
        "",
        `${url.pathname}${url.search}${url.hash}`
      );
    }
  };

  const chooseSuggestion = (suggestion) => {
    input.value = suggestion.value;
    updateResults();
    closeSuggestions();
  };

  input.addEventListener("input", () => {
    updateSuggestions();
    updateResults();
  });
  input.addEventListener("focus", updateSuggestions);
  input.addEventListener("keydown", (event) => {
    if (list.hidden || visibleSuggestions.length === 0) {
      return;
    }

    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActiveSuggestion((activeIndex + 1) % visibleSuggestions.length);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveSuggestion(activeIndex <= 0 ? visibleSuggestions.length - 1 : activeIndex - 1);
    } else if (event.key === "Enter" && activeIndex >= 0) {
      event.preventDefault();
      chooseSuggestion(visibleSuggestions[activeIndex]);
    } else if (event.key === "Escape") {
      event.preventDefault();
      closeSuggestions();
    }
  });

  list.addEventListener("pointerdown", (event) => {
    const element = event.target.closest("[data-search-suggestion]");
    const suggestion = suggestions.find((candidate) => candidate.element === element);
    if (!suggestion) {
      return;
    }

    event.preventDefault();
    chooseSuggestion(suggestion);
  });

  document.addEventListener("pointerdown", (event) => {
    if (!combobox.contains(event.target)) {
      closeSuggestions();
    }
  });

  clearLink.addEventListener("click", (event) => {
    event.preventDefault();
    input.value = "";
    updateSuggestions();
    updateResults();
    input.focus();
  });

  form.addEventListener("submit", closeSuggestions);
  form.querySelector("[data-library-sort]")?.addEventListener("change", () => form.requestSubmit());
  closeSuggestions();
  updateResults(false);
})();
