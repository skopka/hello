(() => {
  "use strict";

  const root = document.querySelector("[data-cross-device-waiting]");
  if (!root) return;

  const statusUrl = root.dataset.statusUrl;
  const interval = Number(root.dataset.pollingInterval || "2000");
  const complete = root.querySelector("[data-cross-device-complete]");
  const countdown = root.querySelector("[data-cross-device-countdown]");
  let submitted = false;

  const updateCountdown = () => {
    if (!countdown) return;
    const expiresAt = Date.parse(countdown.dataset.expiresAt || "");
    if (!Number.isFinite(expiresAt)) return;
    const seconds = Math.max(0, Math.ceil((expiresAt - Date.now()) / 1000));
    countdown.textContent = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
  };

  const poll = async () => {
    try {
      const response = await fetch(statusUrl, {
        credentials: "same-origin",
        headers: { "Accept": "application/json" }
      });
      if (!response.ok) return;
      const status = await response.json();
      if (status.state === "approved" && complete && !submitted) {
        submitted = true;
        complete.hidden = false;
        complete.requestSubmit();
      } else if (status.state === "denied" || status.state === "expired") {
        window.location.reload();
      }
    } catch {
      // A later poll can recover from a transient network failure.
    }
  };

  updateCountdown();
  window.setInterval(updateCountdown, 1000);
  window.setInterval(poll, Math.max(1000, interval));
})();
