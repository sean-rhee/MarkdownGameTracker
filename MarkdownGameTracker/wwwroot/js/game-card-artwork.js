(() => {
  "use strict";

  const grid = document.querySelector("[data-game-artwork-grid]");
  if (!grid) {
    return;
  }

  const cards = Array.from(grid.querySelectorAll("[data-game-card]"));
  if (cards.length === 0) {
    return;
  }

  const selectedIgdbId = (gameId) => {
    try {
      const value = window.localStorage.getItem(`game-garden:igdb-match:${gameId}`);
      const parsed = Number.parseInt(value ?? "", 10);
      return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  };

  const safeArtworkUrl = (value) => {
    try {
      const url = new URL(value);
      return url.protocol === "https:" && url.hostname === "images.igdb.com"
        ? url.toString()
        : null;
    } catch {
      return null;
    }
  };

  const showArtwork = (item) => {
    const card = cards.find((candidate) => candidate.dataset.gameId === item.gameId);
    const artworkUrl = safeArtworkUrl(item.artworkUrl);
    if (!card || !artworkUrl) {
      return;
    }

    const frame = card.querySelector("[data-game-card-artwork]");
    const image = card.querySelector("[data-game-card-image]");
    if (!frame || !image) {
      return;
    }

    frame.dataset.artworkKind = item.kind === "cover" ? "cover" : "artwork";
    image.alt = `Artwork for ${item.matchedTitle ?? card.querySelector("h3")?.textContent ?? "game"}`;
    image.addEventListener("load", () => card.classList.add("has-artwork"), { once: true });
    image.addEventListener("error", () => {
      frame.hidden = true;
      card.classList.remove("has-artwork");
    }, { once: true });
    image.src = artworkUrl;
    frame.hidden = false;
  };

  const loadBatch = async (batch) => {
    const response = await fetch(grid.dataset.artworkUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        games: batch.map((card) => ({
          id: card.dataset.gameId,
          igdbGameId: selectedIgdbId(card.dataset.gameId)
        }))
      })
    });
    if (!response.ok) {
      return;
    }

    const result = await response.json();
    if (result.status !== "available" || !Array.isArray(result.artwork)) {
      return;
    }

    result.artwork.forEach(showArtwork);
  };

  const loadArtwork = async () => {
    for (let offset = 0; offset < cards.length; offset += 100) {
      try {
        await loadBatch(cards.slice(offset, offset + 100));
      } catch {
        // IGDB artwork is optional; text-only cards remain fully usable.
      }
    }
  };

  void loadArtwork();
})();
