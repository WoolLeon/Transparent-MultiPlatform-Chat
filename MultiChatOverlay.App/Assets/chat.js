(() => {
  "use strict";
  const feed = document.getElementById("chat");
  const maxMessages = 40;
  const platformLabels = { twitch: "Twitch", youtube: "YouTube", kick: "Kick", facebook: "Facebook", streamerbot: "Streamer.bot" };
  const platformColors = {
    twitch: "#a970ff", youtube: "#ff4e45", kick: "#53fc18", facebook: "#1877f2", streamerbot: "#8b9bb4"
  };
  const iconPaths = {
    twitch: "M4 3H21L19 17H14L11 21H8V17H4ZM9 7V12H11V7ZM14 7V12H16V7Z",
    youtube: "M22 8.2a2.8 2.8 0 0 0-2-2C18.2 5.7 12 5.7 12 5.7s-6.2 0-8 .5a2.8 2.8 0 0 0-2 2A29 29 0 0 0 1.5 12 29 29 0 0 0 2 16.3a2.8 2.8 0 0 0 2 2c1.8.5 8 .5 8 .5s6.2 0 8-.5a2.8 2.8 0 0 0 2-2 29 29 0 0 0 .5-4.3 29 29 0 0 0-.5-3.8ZM10 15.5v-7l6 3.5Z",
    kick: "M3 3h18v18H3zM7 7v10h3v-4l4 4h4l-6-6 6-4h-5l-3 2V7z",
    facebook: "M13.5 21v-8h2.7l.4-3h-3.1V8.1c0-.9.3-1.5 1.6-1.5h1.7V3.9c-.3 0-1.3-.1-2.5-.1-2.5 0-4.2 1.5-4.2 4.3V10H7.3v3h2.8v8z",
    streamerbot: "M4 4h16v16H4zm3 4v8h2v-3h2v3h2V8h-2v3H9V8zm9 0v8h2V8z"
  };
  const badgePaths = {
    subscriber: "M12 2.5 14.8 8l6.1.9-4.4 4.3 1 6.1-5.5-2.9-5.5 2.9 1-6.1-4.4-4.3L9.2 8z",
    broadcaster: "M3 9 7.5 13 12 5l4.5 8L21 9l-2 11H5z",
    moderator: "M12 2 21 6v5c0 5.2-3.8 9.4-9 11-5.2-1.6-9-5.8-9-11V6zm-1 13 6-6-1.4-1.4-4.6 4.6-2.6-2.6L7 11z"
  };

  function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function addIcon(row, platform) {
    const icon = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    icon.setAttribute("class", `platform-icon platform-${platform}`);
    icon.setAttribute("viewBox", "0 0 24 24");
    icon.setAttribute("role", "img");
    icon.setAttribute("aria-label", platformLabels[platform] || platform);
    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", iconPaths[platform] || "M4 4h16v16H4z");
    path.setAttribute("fill", "currentColor");
    icon.append(path);
    row.append(icon);
  }

  function addBadges(row, message) {
    const kinds = new Set((message.badges || []).map(badge => String(badge.name || "").toLowerCase()));
    if (message.isOwner) kinds.add("broadcaster");
    if (message.isSubscriber) kinds.add("subscriber");
    if (message.isModerator) kinds.add("moderator");

    for (const [kind, pathData] of Object.entries(badgePaths)) {
      if (!kinds.has(kind) && !(kind === "subscriber" && kinds.has("founder"))) continue;
      const badge = document.createElementNS("http://www.w3.org/2000/svg", "svg");
      badge.setAttribute("class", `badge badge-${kind}`);
      badge.setAttribute("viewBox", "0 0 24 24");
      badge.setAttribute("role", "img");
      badge.setAttribute("aria-label", kind);
      badge.setAttribute("title", kind);
      const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
      path.setAttribute("d", pathData);
      badge.append(path);
      row.append(badge);
    }
  }

  function appendPart(container, part) {
    if (part.kind === "emote" && part.url) {
      const image = element("img", "emote");
      image.src = part.url;
      image.alt = part.name || "";
      image.title = part.name || "";
      image.loading = "lazy";
      image.decoding = "async";
      image.addEventListener("load", scrollToLatest, { once: true });
      image.onerror = () => {
        image.replaceWith(document.createTextNode(part.name || ""));
        scrollToLatest();
      };
      container.append(image);
    } else {
      container.append(document.createTextNode(part.text || ""));
    }
  }

  function renderMessage(message) {
    const row = element("div", `message ${String(message.platform).toLowerCase()}`);
    row.dataset.platform = String(message.platform).toLowerCase();
    row.dataset.owner = message.isOwner ? "true" : "false";
    row.dataset.timestamp = String(parseTimestamp(message.timestamp));
    addIcon(row, row.dataset.platform);
    addBadges(row, message);
    const username = element("span", "username", message.displayName || message.username);
    username.style.color = message.userColor || platformColors[row.dataset.platform] || "var(--default-name)";
    row.append(username, document.createTextNode(": "));
    const text = element("span", "message-text");
    for (const part of message.messageParts || []) appendPart(text, part);
    row.append(text);
    insertByTimestamp(row);
  }

  function renderEvent(event) {
    const type = String(event.type || "custom").toLowerCase();
    const row = element("div", `event ${type}`);
    row.dataset.platform = String(event.platform).toLowerCase();
    row.dataset.timestamp = String(parseTimestamp(event.timestamp));
    addIcon(row, row.dataset.platform);
    const amount = event.amount ? ` (${event.amount})` : "";
    row.append(element("strong", "event-icon", type.toUpperCase()), document.createTextNode(` ${event.user}${amount}`));
    if (event.message) row.append(document.createTextNode(`: ${event.message}`));
    insertByTimestamp(row);
  }

  function parseTimestamp(value) {
    const timestamp = Date.parse(value);
    return Number.isFinite(timestamp) ? timestamp : Date.now();
  }

  function insertByTimestamp(row) {
    const timestamp = Number(row.dataset.timestamp);
    const followingRow = Array.from(feed.children).find(
      child => Number(child.dataset.timestamp) > timestamp
    );
    feed.insertBefore(row, followingRow || null);
  }

  function trimFeed() {
    while (feed.children.length > maxMessages) feed.firstElementChild.remove();
  }

  function scrollToLatest() {
    feed.scrollTop = feed.scrollHeight;
  }

  if ("ResizeObserver" in window) {
    new ResizeObserver(() => requestAnimationFrame(scrollToLatest)).observe(feed);
  } else {
    window.addEventListener("resize", scrollToLatest, { passive: true });
  }

  window.chrome.webview.addEventListener("message", event => {
    const item = event.data;
    if (!item) return;
    if (item.clear) feed.replaceChildren();
    else if (item.message) renderMessage(item.message);
    else if (item.event) renderEvent(item.event);
    trimFeed();
    requestAnimationFrame(scrollToLatest);
  });

  window.overlay = {
    clear: () => { feed.replaceChildren(); },
    setCustomCss: css => {
      let custom = document.getElementById("user-css");
      if (!custom) {
        custom = document.createElement("style");
        custom.id = "user-css";
        document.head.append(custom);
      }
      custom.textContent = css || "";
    }
  };
})();
