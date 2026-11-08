(() => {
    const form = document.getElementById("access-link-inspect");
    if (!form) return;
    const inspect = () => {
        const fragment = window.location.hash.slice(1);
        history.replaceState(null, "", window.location.pathname + window.location.search);
        const token = new URLSearchParams(fragment).get("token");
        if (!token || token.length > 512) {
            document.querySelector(".hello-validation.validation-summary-errors")?.focus();
            return;
        }
        form.elements.Token.value = token;
        form.submit();
    };
    window.addEventListener("hashchange", inspect);
    inspect();
})();
