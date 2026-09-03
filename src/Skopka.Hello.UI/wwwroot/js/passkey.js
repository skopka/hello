(() => {
  "use strict";

  // The browser speaks ArrayBuffers and the form speaks strings, so every
  // value crosses this boundary through base64url — the same encoding WebAuthn
  // uses on the wire, and the one the server decodes on the other side.
  const decode = (value) => {
    const padded = value.replace(/-/g, "+").replace(/_/g, "/");
    const raw = atob(padded.padEnd(padded.length + ((4 - (padded.length % 4)) % 4), "="));
    const bytes = new Uint8Array(raw.length);
    for (let index = 0; index < raw.length; index += 1) {
      bytes[index] = raw.charCodeAt(index);
    }
    return bytes;
  };

  const encode = (buffer) => {
    const bytes = new Uint8Array(buffer);
    let raw = "";
    for (const byte of bytes) {
      raw += String.fromCharCode(byte);
    }
    return btoa(raw).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  };

  const supported = () =>
    typeof window.PublicKeyCredential === "function"
    && typeof navigator.credentials?.get === "function";

  const fill = (form, values) => {
    for (const [name, value] of Object.entries(values)) {
      const field = form.querySelector(`[data-passkey-field="${name}"]`);
      if (field) field.value = value;
    }
  };

  const fail = (root) => {
    const error = root.querySelector("[data-passkey-error]");
    if (error) error.hidden = false;
    root.querySelectorAll("[data-passkey-busy]").forEach((element) => {
      element.hidden = true;
    });
    root.querySelectorAll("button").forEach((button) => {
      button.disabled = false;
    });
  };

  const busy = (root) => {
    const error = root.querySelector("[data-passkey-error]");
    if (error) error.hidden = true;
    root.querySelectorAll("[data-passkey-busy]").forEach((element) => {
      element.hidden = false;
    });
    root.querySelectorAll("button").forEach((button) => {
      button.disabled = true;
    });
  };

  // Every ceremony starts with a challenge the server issued and ends with the
  // form it came on. The page never invents either.
  const challenge = async (url, token) => {
    const response = await fetch(url, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "Accept": "application/json",
        "RequestVerificationToken": token
      }
    });
    if (!response.ok) throw new Error("challenge");
    return response.json();
  };

  const start = (root) => {
    if (!supported()) {
      // Nothing is hidden from a browser that cannot do this; the button is
      // simply never shown, so the page keeps whatever else it offers.
      root.hidden = true;
      return;
    }

    root.hidden = false;
    const form = root.querySelector("form[data-passkey-form]");
    const button = root.querySelector("[data-passkey-start]");
    if (!form || !button) return;

    button.addEventListener("click", async (event) => {
      event.preventDefault();
      busy(root);
      try {
        const token = form.querySelector(
          "input[name='__RequestVerificationToken']")?.value ?? "";
        const offer = await challenge(root.dataset.challengeUrl, token);
        if (root.dataset.ceremony === "create") {
          const created = await navigator.credentials.create({
            publicKey: {
              rp: { id: offer.relyingPartyId, name: offer.relyingPartyName },
              user: {
                id: decode(offer.userHandle),
                name: offer.userName,
                displayName: offer.userDisplayName || offer.userName
              },
              challenge: decode(offer.challenge),
              pubKeyCredParams: offer.algorithms.map((alg) => ({
                type: "public-key",
                alg
              })),
              // The keys already registered, so an authenticator holding one
              // for this account makes another rather than replacing it.
              excludeCredentials: offer.excludeCredentials.map((id) => ({
                type: "public-key",
                id: decode(id)
              })),
              authenticatorSelection: {
                userVerification: offer.userVerificationRequired
                  ? "required"
                  : "preferred",
                residentKey: "preferred"
              },
              // Not asked for, because the server does not judge authenticator
              // models and would only be throwing the answer away.
              attestation: "none",
              timeout: 120000
            }
          });
          if (!created) throw new Error("cancelled");
          fill(form, {
            ticket: offer.ticket,
            clientDataJson: encode(created.response.clientDataJSON),
            attestationObject: encode(created.response.attestationObject)
          });
        } else {
          const asserted = await navigator.credentials.get({
            publicKey: {
              rpId: offer.relyingPartyId,
              challenge: decode(offer.challenge),
              // No credential is listed: the passkey names itself, which is
              // what lets someone sign in having typed nothing.
              userVerification: offer.userVerificationRequired
                ? "required"
                : "preferred",
              timeout: 120000
            }
          });
          if (!asserted) throw new Error("cancelled");
          fill(form, {
            ticket: offer.ticket,
            credentialId: encode(asserted.rawId),
            clientDataJson: encode(asserted.response.clientDataJSON),
            authenticatorData: encode(asserted.response.authenticatorData),
            signature: encode(asserted.response.signature)
          });
        }

        form.requestSubmit();
      } catch {
        // A cancelled prompt and a refused challenge look the same from here,
        // and the page says the same thing about both: it did not work, try
        // again. What went wrong is the server's to know.
        fail(root);
      }
    });
  };

  document.querySelectorAll("[data-passkey]").forEach(start);
})();
