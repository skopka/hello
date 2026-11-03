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
