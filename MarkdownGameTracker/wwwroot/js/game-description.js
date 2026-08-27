(() => {
  const card = document.querySelector("[data-game-description]");
  if (!card) {
    return;
  }

  const body = card.querySelector("[data-description-body]");
  const footer = card.querySelector("[data-description-footer]");
  const match = card.querySelector("[data-description-match]");
  const source = card.querySelector("[data-description-source]");

  const showMessage = (message, state) => {
    body.textContent = message;
    card.dataset.descriptionState = state;
  };

  const loadDescription = async () => {
    try {
      const response = await fetch(card.dataset.descriptionUrl, {
        headers: { "Accept": "application/json" }
      });
      if (!response.ok) {
        throw new Error(`Description request failed with ${response.status}.`);
      }

      const result = await response.json();
      switch (result.status) {
        case "available":
          showMessage(result.description, "available");
          match.textContent = result.matchedTitle
            ? `Matched as ${result.matchedTitle} · not stored in your note`
            : "Fetched live · not stored in your note";
          if (result.sourceUrl) {
            source.href = result.sourceUrl;
            source.hidden = false;
          }
          footer.hidden = false;
          break;
        case "notConfigured":
          showMessage("Add IGDB credentials to enable game descriptions.", "not-configured");
          break;
        case "notFound":
          showMessage("IGDB did not return a description for this title.", "not-found");
          break;
        default:
          showMessage("The IGDB description is temporarily unavailable.", "unavailable");
          break;
      }
    } catch {
      showMessage("The IGDB description is temporarily unavailable.", "unavailable");
    }
  };

  loadDescription();
})();
