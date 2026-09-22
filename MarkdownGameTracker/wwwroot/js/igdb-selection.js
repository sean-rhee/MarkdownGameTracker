(() => {
  "use strict";

  const keyFor = (gameId) => `game-garden:igdb-match:${gameId}`;

  const get = (gameId) => {
    try {
      const value = window.localStorage.getItem(keyFor(gameId));
      const parsed = Number.parseInt(value ?? "", 10);
      return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  };

  const set = (gameId, igdbGameId) => {
    try {
      window.localStorage.setItem(keyFor(gameId), String(igdbGameId));
    } catch {
      // The current page can still use the choice when storage is unavailable.
    }
  };

  const clear = (gameId) => {
    try {
      window.localStorage.removeItem(keyFor(gameId));
    } catch {
      // There is no fallback cleanup to perform when storage is unavailable.
    }
  };

  // Only successful form redirects emit a transfer, using the actual saved ID.
  const transfer = document.querySelector("[data-igdb-selection-transfer]");
  if (transfer) {
    const { targetId, sourceId, selectedId } = transfer.dataset;
    const explicitId = Number(selectedId);
    const igdbId = Number.isSafeInteger(explicitId) && explicitId > 0 ? explicitId : sourceId ? get(sourceId) : null;
    if (targetId && igdbId) {
      set(targetId, igdbId);
      if (sourceId && sourceId !== targetId && get(targetId) === igdbId) clear(sourceId);
    }
  }

  window.GameGardenIgdbSelection = Object.freeze({ get, set, clear });
})();
