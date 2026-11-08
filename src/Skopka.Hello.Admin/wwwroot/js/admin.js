document.addEventListener("DOMContentLoaded", () => {
  const dialog = document.querySelector('[data-admin-auto-open="true"]');
  if (!dialog || !window.bootstrap) return;

  const target = dialog.dataset.adminReturn || `#${dialog.id}`;
  const trigger = Array.from(document.querySelectorAll('[data-bs-toggle="modal"]'))
    .find(button => button.getAttribute("data-bs-target") === target);
  dialog.addEventListener("hidden.bs.modal", () => {
    const fallback = document.querySelector(".admin-page-header h1");
    if (trigger) trigger.focus();
    else if (fallback) {
      fallback.tabIndex = -1;
      fallback.focus();
    }
  }, { once: true });
  bootstrap.Modal.getOrCreateInstance(dialog).show(trigger);
});

// A confirmation is sent once: a second Enter or click while the first
// submission is on its way is ignored. The server also treats a duplicate of
// one confirmation like the first, so this only spares a wasted request.
document.addEventListener("submit", event => {
  const form = event.target;
  if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() !== "post") return;
  if (form.dataset.adminSubmitting === "true") {
    event.preventDefault();
    return;
  }
  form.dataset.adminSubmitting = "true";
});

// Back/forward cache restores the page as it was when it was left.
window.addEventListener("pageshow", () => {
  document.querySelectorAll("form[data-admin-submitting]")
    .forEach(form => delete form.dataset.adminSubmitting);
});
