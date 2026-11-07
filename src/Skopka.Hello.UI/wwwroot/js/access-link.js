(() => {
    const form = document.getElementById("access-link-inspect");
    if (!form) return;
    const fragment = window.location.hash.slice(1);
    history.replaceState(null, "", window.location.pathname + window.location.search);
    const token = new URLSearchParams(fragment).get("token");
    if (!token || token.length > 512) return;
    form.elements.Token.value = token;
    form.submit();
})();
