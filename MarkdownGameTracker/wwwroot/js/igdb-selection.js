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

  window.GameGardenIgdbSelection = Object.freeze({ get, set, clear });
})();
