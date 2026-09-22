(() => {
  window.GameGardenMedia = Object.freeze({ create: () => {
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

    return Object.freeze({ clear: clearMedia, render: renderMedia });
  }});
})();
