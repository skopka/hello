(() => {
    "use strict";

    const form = document.querySelector(
        "form[data-hello-auto-submit='true']");
    if (!(form instanceof HTMLFormElement)) {
        return;
    }

    const status = document.querySelector(
        "[data-hello-confirmation-status]");

    try {
        form.setAttribute("aria-busy", "true");

        if (window.history && window.history.replaceState) {
            window.history.replaceState(
                window.history.state,
                document.title,
                window.location.pathname);
        }

        if (typeof form.requestSubmit === "function") {
            form.requestSubmit();
        } else {
            form.submit();
        }
    } catch {
        form.removeAttribute("aria-busy");
        if (status) {
            status.textContent =
                "Automatic confirmation could not be started. " +
                "Use the button below to confirm your email.";
        }
    }
})();
