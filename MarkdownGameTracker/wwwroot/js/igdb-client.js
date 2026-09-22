(() => {
  const getJson = async (url) => {
    const response = await fetch(url, { headers: { Accept: "application/json" } });
    if (!response.ok) throw new Error(`IGDB request failed with ${response.status}.`);
    return response.json();
  };
  window.GameGardenIgdbClient = Object.freeze({ getJson });
})();
