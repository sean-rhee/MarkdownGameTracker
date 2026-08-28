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
  const mediaPanel = document.querySelector("[data-game-media]");
  const heroFrame = mediaPanel.querySelector("[data-game-hero-frame]");
  const heroImage = mediaPanel.querySelector("[data-game-hero]");
  const coverImage = mediaPanel.querySelector("[data-game-cover]");
  const artworkCredit = mediaPanel.querySelector("[data-artwork-credit]");
  const mediaCollection = document.querySelector("[data-game-media-collection]");
  const screenshotSection = mediaCollection.querySelector("[data-game-screenshots]");
  const screenshotGrid = mediaCollection.querySelector("[data-game-screenshot-grid]");
  const viewAllScreenshots = mediaCollection.querySelector("[data-view-all-screenshots]");
  const screenshotCount = mediaCollection.querySelector("[data-screenshot-count]");
  const videoSection = mediaCollection.querySelector("[data-game-video]");
  const videoCarousel = mediaCollection.querySelector("[data-game-video-carousel]");
  const videoCount = mediaCollection.querySelector("[data-video-count]");
  const screenshotViewer = document.querySelector("[data-game-screenshot-viewer]");
  const screenshotViewerImage = screenshotViewer?.querySelector("[data-screenshot-viewer-image]");
  const screenshotCounter = screenshotViewer?.querySelector("[data-screenshot-counter]");
  const previousScreenshot = screenshotViewer?.querySelector("[data-screenshot-previous]");
  const nextScreenshot = screenshotViewer?.querySelector("[data-screenshot-next]");
  const gameTitle = mediaPanel.dataset.gameTitle;
  const gameId = card.dataset.gameId;
  const selectionStore = window.GameGardenIgdbSelection;
  let matchesLoaded = false;
  let matchesLoading = false;
  let selectedIgdbId = selectionStore.get(gameId);
  let viewerScreenshots = [];
  let currentScreenshotIndex = 0;

  const renderScreenshotViewer = () => {
    const screenshot = viewerScreenshots[currentScreenshotIndex];
    if (!screenshot || !screenshotViewerImage || !screenshotCounter) {
      return;
    }

    screenshotViewerImage.src = screenshot.fullSizeUrl;
    screenshotViewerImage.alt = screenshot.alt;
    screenshotCounter.textContent = `${currentScreenshotIndex + 1} of ${viewerScreenshots.length}`;

    const hasMultipleScreenshots = viewerScreenshots.length > 1;
    previousScreenshot.hidden = !hasMultipleScreenshots;
    nextScreenshot.hidden = !hasMultipleScreenshots;
  };

  const moveScreenshot = (offset) => {
    if (viewerScreenshots.length < 2) {
      return;
    }

    currentScreenshotIndex = (
      currentScreenshotIndex + offset + viewerScreenshots.length
    ) % viewerScreenshots.length;
    renderScreenshotViewer();
  };

  const openScreenshotViewer = (index) => {
    if (!screenshotViewer || !window.bootstrap?.Modal) {
      return false;
    }

    currentScreenshotIndex = index;
    renderScreenshotViewer();
    window.bootstrap.Modal.getOrCreateInstance(screenshotViewer).show();
    return true;
  };

  const showMessage = (message, state) => {
    body.textContent = message;
    card.dataset.descriptionState = state;
  };

  const getSafeVideoEmbedUrl = (value) => {
    try {
      const url = new URL(value);
      return url.protocol === "https:"
        && url.hostname === "www.youtube-nocookie.com"
        && /^\/embed\/[A-Za-z0-9_-]{11}$/.test(url.pathname)
        ? url.href
        : null;
    } catch {
      return null;
    }
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

  const clearMedia = () => {
    heroFrame.dataset.hasHero = "false";
    heroImage.hidden = true;
    heroImage.removeAttribute("src");
    coverImage.hidden = true;
    coverImage.removeAttribute("src");
    artworkCredit.hidden = true;
    mediaCollection.hidden = true;
    screenshotSection.hidden = true;
    screenshotGrid.replaceChildren();
    viewAllScreenshots.hidden = true;
    screenshotCount.textContent = "";
    videoSection.hidden = true;
    videoCarousel.replaceChildren();
    videoCount.textContent = "";
    viewerScreenshots = [];
  };

  const renderMedia = (result) => {
    clearMedia();
    const artworks = Array.isArray(result.artworks) ? result.artworks : [];
    const screenshots = Array.isArray(result.screenshots) ? result.screenshots : [];
    const images = [
      ...artworks.map((image, index) => ({ ...image, kind: "Artwork", kindIndex: index + 1 })),
      ...screenshots.map((image, index) => ({ ...image, kind: "Screenshot", kindIndex: index + 1 }))
    ].filter((image, index, collection) =>
      image.fullSizeUrl
      && collection.findIndex(candidate => candidate.fullSizeUrl === image.fullSizeUrl) === index);
    const videos = (Array.isArray(result.videos) ? result.videos : [])
      .map(video => ({
        name: video.name?.trim() || "Game video",
        embedUrl: getSafeVideoEmbedUrl(video.embedUrl)
      }))
      .filter(video => video.embedUrl);
    if (!result.heroUrl && !result.coverUrl && images.length === 0 && videos.length === 0) {
      return;
    }

    if (result.heroUrl) {
      heroImage.src = result.heroUrl;
      heroImage.hidden = false;
      heroFrame.dataset.hasHero = "true";
    }

    if (result.coverUrl) {
      coverImage.src = result.coverUrl;
      coverImage.alt = `Cover art for ${result.matchedTitle ?? gameTitle}`;
      coverImage.hidden = false;
    }

    artworkCredit.hidden = false;

    if (videos.length > 0) {
      for (const [index, video] of videos.entries()) {
        const item = document.createElement("article");
        item.className = "game-video-item";

        const frame = document.createElement("div");
        frame.className = "game-video-frame";

        const iframe = document.createElement("iframe");
        iframe.src = video.embedUrl;
        iframe.title = `${video.name} for ${result.matchedTitle ?? gameTitle}`;
        iframe.loading = index === 0 ? "eager" : "lazy";
        iframe.referrerPolicy = "strict-origin-when-cross-origin";
        iframe.allow = "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share";
        iframe.allowFullscreen = true;

        const title = document.createElement("h4");
        title.className = "game-video-item-title";
        title.textContent = video.name;

        frame.append(iframe);
        item.append(frame, title);
        videoCarousel.append(item);
      }

      videoCount.textContent = String(videos.length);
      videoSection.hidden = false;
    }

    if (images.length > 0) {
      viewerScreenshots = images.map(image => ({
        fullSizeUrl: image.fullSizeUrl,
        alt: `${image.kind} ${image.kindIndex} from ${result.matchedTitle ?? gameTitle}`
      }));

      for (const [index, imageItem] of images.entries()) {
        const link = document.createElement("a");
        link.className = "game-screenshot-link";
        link.href = imageItem.fullSizeUrl;
        link.target = "_blank";
        link.rel = "noopener noreferrer";
        link.setAttribute(
          "aria-label",
          `View ${imageItem.kind.toLowerCase()} ${imageItem.kindIndex} from ${result.matchedTitle ?? gameTitle}`);
        link.addEventListener("click", (event) => {
          if (openScreenshotViewer(index)) {
            event.preventDefault();
          }
        });

        const image = document.createElement("img");
        image.src = imageItem.thumbnailUrl;
        image.alt = `${imageItem.kind} ${imageItem.kindIndex} from ${result.matchedTitle ?? gameTitle}`;
        image.loading = "lazy";
        image.referrerPolicy = "no-referrer";
        link.append(image);
        screenshotGrid.append(link);
      }

      screenshotSection.hidden = false;
      screenshotCount.textContent = `(${images.length})`;
      viewAllScreenshots.hidden = false;
    }

    mediaCollection.hidden = images.length === 0 && videos.length === 0;
  };

  viewAllScreenshots.addEventListener("click", () => openScreenshotViewer(0));
  previousScreenshot?.addEventListener("click", () => moveScreenshot(-1));
  nextScreenshot?.addEventListener("click", () => moveScreenshot(1));
  screenshotViewer?.addEventListener("keydown", (event) => {
    if (event.key === "ArrowLeft") {
      event.preventDefault();
      moveScreenshot(-1);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      moveScreenshot(1);
    }
  });

  const renderDescription = (result, requestedIgdbId) => {
    switch (result.status) {
      case "available":
        showMessage(
          result.description ?? "IGDB does not have a description for this game yet.",
          "available");
        renderMedia(result);
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
        clearMedia();
        showMessage("Add IGDB credentials to enable game descriptions.", "not-configured");
        footer.hidden = true;
        break;
      case "notFound":
        clearMedia();
        showMessage("IGDB did not return a description for this title.", "not-found");
        showChooser("No description found automatically · not stored in your note");
        break;
      default:
        clearMedia();
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
        selectionStore.clear(gameId);
        await loadDescription(null, false);
        return;
      }

      renderDescription(result, igdbGameId);
    } catch {
      clearMedia();
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
    selectionStore.set(gameId, selectedIgdbId);
    matchPicker.hidden = true;
    matchToggle.setAttribute("aria-expanded", "false");
    matchToggle.textContent = "Choose another match";
    clearMedia();
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
