(() => {
  const form = document.querySelector(
    "[data-cross-device-begin-approval]");
  if (!form || form.dataset.submitted === "true") {
    return;
  }

  form.dataset.submitted = "true";
  const button = form.querySelector("button[type='submit']");
  if (button) {
    button.disabled = true;
  }

  form.requestSubmit();
})();
