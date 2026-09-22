(() => {
  window.GameGardenMatchPicker = Object.freeze({ create: (card, getSelectedId, onChoose) => {
    const matchToggle = card.querySelector("[data-match-toggle]");
    const matchPicker = card.querySelector("[data-match-picker]");
    const matchList = card.querySelector("[data-match-list]");
    let matchesLoaded = false;
    let matchesLoading = false;
    const hide = () => {
      matchPicker.hidden = true;
      matchToggle.setAttribute("aria-expanded", "false");
      matchToggle.textContent = "Choose another match";
    };
    const setMatchListMessage = (message) => {
      matchList.replaceChildren();
      const paragraph = document.createElement("p");
      paragraph.className = "igdb-match-message";
      paragraph.textContent = message;
      matchList.append(paragraph);
    };

    const renderMatches = (candidates) => {
      matchList.replaceChildren();
      for (const candidate of candidates) {
        const button = document.createElement("button");
        button.className = "igdb-match-option";
        button.type = "button";
        if (candidate.igdbGameId === getSelectedId()) {
          button.setAttribute("aria-current", "true");
        }

        if (candidate.coverUrl) {
          const cover = document.createElement("img");
          cover.className = "igdb-match-cover";
          cover.src = candidate.coverUrl;
          cover.alt = "";
          cover.loading = "lazy";
          cover.referrerPolicy = "no-referrer";
          button.append(cover);
        }

        const details = document.createElement("span");
        details.className = "igdb-match-details";

        const title = document.createElement("span");
        title.className = "igdb-match-title";
        title.textContent = candidate.title;

        const year = document.createElement("span");
        year.className = "igdb-match-year";
        year.textContent = candidate.releaseYear ?? "Year unknown";

        details.append(title, year);
        button.append(details);
        button.addEventListener("click", () => { hide(); onChoose(candidate); });
        matchList.append(button);
      }
    };

    const loadMatches = async () => {
      setMatchListMessage("Searching IGDB…");
      try {
        const result = await window.GameGardenIgdbClient.getJson(card.dataset.matchesUrl);
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

    return Object.freeze({ hide });
  }});
})();
