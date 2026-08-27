(() => {
  const card = document.querySelector("[data-game-description]");
  if (!card) {
    return;
  }

  const body = card.querySelector("[data-description-body]");
  const footer = card.querySelector("[data-description-footer]");
  const match = card.querySelector("[data-description-match]");
  const source = card.querySelector("[data-description-source]");
  const matchToggle = card.querySelector("[data-match-toggle]");
  const matchPicker = card.querySelector("[data-match-picker]");
  const matchList = card.querySelector("[data-match-list]");
  const storageKey = `game-garden:igdb-match:${card.dataset.gameId}`;
  let matchesLoaded = false;
  let matchesLoading = false;
  let selectedIgdbId = readStoredSelection();

  function readStoredSelection() {
    try {
      const storedValue = window.localStorage.getItem(storageKey);
      const parsedValue = Number.parseInt(storedValue, 10);
      return Number.isSafeInteger(parsedValue) && parsedValue > 0 ? parsedValue : null;
    } catch {
      return null;
    }
  }

  function storeSelection(igdbGameId) {
    try {
      window.localStorage.setItem(storageKey, String(igdbGameId));
    } catch {
      // The choice still applies for this page when browser storage is unavailable.
    }
  }

  function clearStoredSelection() {
    try {
      window.localStorage.removeItem(storageKey);
    } catch {
      // Nothing else is required when browser storage is unavailable.
    }
  }

  const showMessage = (message, state) => {
    body.textContent = message;
    card.dataset.descriptionState = state;
  };

  const getDescriptionUrl = (igdbGameId) => {
    const url = new URL(card.dataset.descriptionUrl, window.location.origin);
    if (igdbGameId) {
      url.searchParams.set("igdbId", String(igdbGameId));
    }

    return `${url.pathname}${url.search}`;
  };

  const hideSource = () => {
    source.hidden = true;
    source.removeAttribute("href");
  };

  const showChooser = (message) => {
    match.textContent = message;
    matchToggle.hidden = false;
    hideSource();
    footer.hidden = false;
  };

  const renderDescription = (result, requestedIgdbId) => {
    switch (result.status) {
      case "available":
        showMessage(result.description, "available");
        selectedIgdbId = result.igdbGameId;
        match.textContent = requestedIgdbId
          ? `Selected as ${result.matchedTitle} · remembered in this browser · not stored in your note`
          : `Matched as ${result.matchedTitle} · not stored in your note`;
        matchToggle.hidden = false;
        if (result.sourceUrl) {
          source.href = result.sourceUrl;
          source.hidden = false;
        } else {
          hideSource();
        }
        footer.hidden = false;
        break;
      case "notConfigured":
        showMessage("Add IGDB credentials to enable game descriptions.", "not-configured");
        footer.hidden = true;
        break;
      case "notFound":
        showMessage("IGDB did not return a description for this title.", "not-found");
        showChooser("No description found automatically · not stored in your note");
        break;
      default:
        showMessage("The IGDB description is temporarily unavailable.", "unavailable");
        footer.hidden = true;
        break;
    }
  };

  const loadDescription = async (igdbGameId = null, retryAutomatic = true) => {
    try {
      const response = await fetch(getDescriptionUrl(igdbGameId), {
        headers: { "Accept": "application/json" }
      });
      if (!response.ok) {
        throw new Error(`Description request failed with ${response.status}.`);
      }

      const result = await response.json();
      if (igdbGameId && result.status === "notFound" && retryAutomatic) {
        selectedIgdbId = null;
        clearStoredSelection();
        await loadDescription(null, false);
        return;
      }

      renderDescription(result, igdbGameId);
    } catch {
      showMessage("The IGDB description is temporarily unavailable.", "unavailable");
      footer.hidden = true;
    }
  };

  const setMatchListMessage = (message) => {
    matchList.replaceChildren();
    const paragraph = document.createElement("p");
    paragraph.className = "igdb-match-message";
    paragraph.textContent = message;
    matchList.append(paragraph);
  };

  const chooseMatch = async (candidate) => {
    selectedIgdbId = candidate.igdbGameId;
    storeSelection(selectedIgdbId);
    matchPicker.hidden = true;
    matchToggle.setAttribute("aria-expanded", "false");
    matchToggle.textContent = "Choose another match";
    showMessage(`Loading ${candidate.title}…`, "loading");
    footer.hidden = true;
    await loadDescription(selectedIgdbId);
  };

  const renderMatches = (candidates) => {
    matchList.replaceChildren();
    for (const candidate of candidates) {
      const button = document.createElement("button");
      button.className = "igdb-match-option";
      button.type = "button";
      if (candidate.igdbGameId === selectedIgdbId) {
        button.setAttribute("aria-current", "true");
      }

      const title = document.createElement("span");
      title.className = "igdb-match-title";
      title.textContent = candidate.title;

      const year = document.createElement("span");
      year.className = "igdb-match-year";
      year.textContent = candidate.releaseYear ?? "Year unknown";

      button.append(title, year);
      button.addEventListener("click", () => chooseMatch(candidate));
      matchList.append(button);
    }
  };

  const loadMatches = async () => {
    setMatchListMessage("Searching IGDB…");
    try {
      const response = await fetch(card.dataset.matchesUrl, {
        headers: { "Accept": "application/json" }
      });
      if (!response.ok) {
        throw new Error(`Match request failed with ${response.status}.`);
      }

      const result = await response.json();
      if (result.status === "available" && result.matches.length > 0) {
        renderMatches(result.matches);
        matchesLoaded = true;
      } else if (result.status === "notConfigured") {
        setMatchListMessage("Add IGDB credentials to search for another match.");
      } else if (result.status === "notFound") {
        setMatchListMessage("No other IGDB titles were found for this note.");
        matchesLoaded = true;
      } else {
        setMatchListMessage("IGDB search is temporarily unavailable.");
      }
    } catch {
      setMatchListMessage("IGDB search is temporarily unavailable.");
    }
  };

  matchToggle.addEventListener("click", async () => {
    const shouldOpen = matchPicker.hidden;
    matchPicker.hidden = !shouldOpen;
    matchToggle.setAttribute("aria-expanded", String(shouldOpen));
    matchToggle.textContent = shouldOpen ? "Hide matches" : "Choose another match";

    if (shouldOpen && !matchesLoaded && !matchesLoading) {
      matchesLoading = true;
      try {
        await loadMatches();
      } finally {
        matchesLoading = false;
      }
    }
  });

  loadDescription(selectedIgdbId);
})();
