document.querySelectorAll('[data-phone-resend]').forEach((button) => {
  let remaining = Number(button.dataset.retrySeconds || 0);
  if (!Number.isFinite(remaining) || remaining <= 0) return;

  const original = button.textContent.trim();
  const label = button.dataset.countdownLabel || 'Resend in {seconds}s';
  const tick = () => {
    button.disabled = remaining > 0;
    button.textContent = remaining > 0
      ? label.replace('{seconds}', String(remaining))
      : original;
    remaining -= 1;
  };
  tick();
  const timer = window.setInterval(() => {
    tick();
    if (remaining < 0) window.clearInterval(timer);
  }, 1000);
});
