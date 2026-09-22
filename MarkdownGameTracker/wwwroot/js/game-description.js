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
  const media = window.GameGardenMedia.create();
  const gameId = card.dataset.gameId;
  const selectionStore = window.GameGardenIgdbSelection;
  let selectedIgdbId = selectionStore.get(gameId);
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
        showMessage(
          result.description ?? "IGDB does not have a description for this game yet.",
          "available");
        media.render(result);
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
        media.clear();
        showMessage("Add IGDB credentials to enable game descriptions.", "not-configured");
        footer.hidden = true;
        break;
      case "notFound":
        media.clear();
        showMessage("IGDB did not return a description for this title.", "not-found");
        showChooser("No description found automatically · not stored in your note");
        break;
      default:
        media.clear();
        showMessage("The IGDB description is temporarily unavailable.", "unavailable");
        footer.hidden = true;
        break;
    }
  };

  const loadDescription = async (igdbGameId = null, retryAutomatic = true) => {
    try {
      const result = await window.GameGardenIgdbClient.getJson(getDescriptionUrl(igdbGameId));
      if (igdbGameId && result.status === "notFound" && retryAutomatic) {
        selectedIgdbId = null;
        selectionStore.clear(gameId);
        await loadDescription(null, false);
        return;
      }

      renderDescription(result, igdbGameId);
    } catch {
      media.clear();
      showMessage("The IGDB description is temporarily unavailable.", "unavailable");
      footer.hidden = true;
    }
  };

  const chooseMatch = async (candidate) => {
    selectedIgdbId = candidate.igdbGameId;
    selectionStore.set(gameId, selectedIgdbId);
    media.clear();
    showMessage(`Loading ${candidate.title}…`, "loading");
    footer.hidden = true;
    await loadDescription(selectedIgdbId);
  };

  window.GameGardenMatchPicker.create(card, () => selectedIgdbId, chooseMatch);

  loadDescription(selectedIgdbId);
})();
