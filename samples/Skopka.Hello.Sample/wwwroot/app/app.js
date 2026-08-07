const state = {
  accessToken: null,
  config: null,
  oidcChannel: null,
  oidcAction: null,
  pendingMutation: null,
  providers: [],
};

const elements = {
  accountPanel: document.querySelector("#account-panel"),
  cancelFlow: document.querySelector("#cancel-flow"),
  externalLogins: document.querySelector("#external-logins"),
  passwordLoginForm: document.querySelector("#password-login-form"),
  providerButtons: document.querySelector("#provider-buttons"),
  registrationForm: document.querySelector("#registration-form"),
  registrationPanel: document.querySelector("#registration-panel"),
  registrationProvider: document.querySelector("#registration-provider"),
  sessionExpiry: document.querySelector("#session-expiry"),
  sessionId: document.querySelector("#session-id"),
  signInPanel: document.querySelector("#sign-in-panel"),
  status: document.querySelector("#status"),
  verificationForm: document.querySelector("#verification-form"),
  verificationPanel: document.querySelector("#verification-panel"),
  verificationTitle: document.querySelector("#verification-title"),
};

await initialize();

async function initialize() {
  try {
    state.config = await request("/app/config");
    if (location.pathname === state.config.oidcReturnPath) {
      completePopupNavigation();
      return;
    }

    bindEvents();
    state.providers = await request("/auth/external/providers");
    renderSignInProviders();
    setStatus("Choose an external provider or use a password account.");
  } catch (error) {
    showError(error);
  }
}

function bindEvents() {
  elements.passwordLoginForm.addEventListener(
    "submit",
    event => void signInWithPassword(event));
  elements.registrationForm.addEventListener(
    "submit",
    event => void registerExternalIdentity(event));
  elements.verificationForm.addEventListener(
    "submit",
    event => void completeVerification(event));
  elements.cancelFlow.addEventListener(
    "click",
    () => void cancelExternalFlow());
}

function completePopupNavigation() {
  const channelId = new URLSearchParams(location.search).get("channel");
  document.body.textContent = "Returning to the application…";
  if (!channelId) {
    document.body.textContent = "The OIDC return channel is missing.";
    return;
  }

  const channel = new BroadcastChannel(channelName(channelId));
  channel.postMessage({ type: "skopka-hello:oidc-return" });
  channel.close();
  window.close();
}

function renderSignInProviders() {
  elements.providerButtons.replaceChildren();
  for (const provider of state.providers) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = `Sign in with ${provider.displayName}`;
    button.addEventListener(
      "click",
      () => beginExternalSignIn(provider.providerId));
    elements.providerButtons.append(button);
  }
}

function beginExternalSignIn(providerId) {
  const returnUrl = prepareOidcReturn("sign-in");
  const challengeUrl =
    `/auth/external/${encodeURIComponent(providerId)}/challenge`
    + `?returnUrl=${encodeURIComponent(returnUrl)}`;
  openProviderPopup(challengeUrl);
}

async function signInWithPassword(event) {
  event.preventDefault();
  const form = new FormData(elements.passwordLoginForm);
  await run(async () => {
    const session = await request("/auth/login", {
      body: {
        login: form.get("login"),
        password: form.get("password"),
      },
      method: "POST",
    });
    await acceptSession(session, "Signed in with a password account.");
    elements.passwordLoginForm.reset();
  });
}

async function finishExternalNavigation() {
  const action = state.oidcAction;
  closeOidcChannel();
  if (!action) {
    return;
  }

  await run(async () => {
    const completion = await request("/auth/external/complete", {
      authenticated: action === "link",
      csrf: true,
      method: "POST",
    });

    switch (completion.outcome) {
      case "SignedIn":
        await acceptSession(
          completion.session,
          "External sign-in completed.");
        break;
      case "RegistrationRequired":
        showRegistration(completion.registration);
        setStatus("Complete the local account profile.");
        break;
      case "LinkVerificationRequired":
        await request("/account/external-logins/link/challenge", {
          authenticated: true,
          csrf: true,
          method: "POST",
        });
        showVerification("link");
        setStatus("A linking verification code was sent.");
        break;
      default:
        throw new Error("The server returned an unknown OIDC outcome.");
    }
  });
}

function showRegistration(hints) {
  const form = elements.registrationForm.elements;
  elements.registrationProvider.textContent =
    `Provider: ${hints.provider.displayName}`;
  form.email.value = hints.verifiedEmail ?? "";
  form.displayName.value = hints.displayName ?? "";
  form.locale.value = hints.locale ?? "";
  elements.registrationPanel.hidden = false;
  elements.signInPanel.hidden = true;
}

async function registerExternalIdentity(event) {
  event.preventDefault();
  const form = new FormData(elements.registrationForm);
  await run(async () => {
    const completed = await request("/auth/external/registration", {
      body: {
        email: emptyToNull(form.get("email")),
        phone: emptyToNull(form.get("phone")),
        profile: {
          displayName: form.get("displayName"),
          locale: emptyToNull(form.get("locale")),
        },
        userName: form.get("userName"),
      },
      csrf: true,
      method: "POST",
    });
    if (completed.outcome !== "SignedIn" || !completed.session) {
      throw new Error("External registration did not create a session.");
    }

    elements.registrationForm.reset();
    elements.registrationPanel.hidden = true;
    await acceptSession(completed.session, "External account created.");
  });
}

async function cancelExternalFlow() {
  await run(async () => {
    await request("/auth/external/flow", {
      csrf: true,
      method: "DELETE",
    });
    elements.registrationForm.reset();
    elements.registrationPanel.hidden = true;
    elements.signInPanel.hidden = false;
    setStatus("External flow cancelled.");
  });
}

async function acceptSession(session, message) {
  if (!session?.accessToken) {
    throw new Error("The server did not return an access token.");
  }

  state.accessToken = session.accessToken;
  elements.sessionId.textContent = session.sessionId;
  elements.sessionExpiry.textContent = new Date(
    session.accessTokenExpiresAt).toLocaleString();
  elements.signInPanel.hidden = true;
  elements.registrationPanel.hidden = true;
  elements.accountPanel.hidden = false;
  await request("/auth/antiforgery", { authenticated: true });
  await loadExternalLogins();
  setStatus(message);
}

async function loadExternalLogins() {
  const linked = await request("/account/external-logins", {
    authenticated: true,
  });
  const linkedByProvider = new Map(
    linked.map(provider => [provider.providerId, provider]));
  elements.externalLogins.replaceChildren();

  for (const provider of state.providers) {
    const row = document.createElement("div");
    row.className = "provider-row";
    const label = document.createElement("span");
    label.textContent = provider.displayName;
    row.append(label);

    const existing = linkedByProvider.get(provider.providerId);
    if (!existing) {
      const link = document.createElement("button");
      link.type = "button";
      link.textContent = `Link ${provider.displayName}`;
      link.addEventListener(
        "click",
        () => void beginLink(provider.providerId));
      row.append(link);
    } else if (existing.canUnlink) {
      const unlink = document.createElement("button");
      unlink.type = "button";
      unlink.className = "danger";
      unlink.textContent = `Unlink ${provider.displayName}`;
      unlink.addEventListener(
        "click",
        () => void beginUnlink(provider.providerId));
      row.append(unlink);
    } else {
      const status = document.createElement("span");
      status.className = "muted";
      status.textContent = "Linked (required sign-in method)";
      row.append(status);
    }

    elements.externalLogins.append(row);
  }
}

async function beginLink(providerId) {
  await run(async () => {
    const returnUrl = prepareOidcReturn("link");
    try {
      const start = await request(
        `/account/external-logins/${encodeURIComponent(providerId)}/link`,
        {
          authenticated: true,
          body: { returnUrl },
          csrf: true,
          method: "POST",
        });
      openProviderPopup(start.challengeUrl);
    } catch (error) {
      closeOidcChannel();
      throw error;
    }
  });
}

async function beginUnlink(providerId) {
  await run(async () => {
    await request(
      `/account/external-logins/${encodeURIComponent(providerId)}`
        + "/unlink/challenge",
      {
        authenticated: true,
        csrf: true,
        method: "POST",
      });
    showVerification("unlink");
    setStatus("An unlinking verification code was sent.");
  });
}

function showVerification(action) {
  state.pendingMutation = action;
  elements.verificationTitle.textContent = action === "link"
    ? "Verify external login linking"
    : "Verify external login unlinking";
  elements.verificationPanel.hidden = false;
  elements.verificationForm.elements.verificationCode.focus();
}

async function completeVerification(event) {
  event.preventDefault();
  const action = state.pendingMutation;
  if (!action) {
    return;
  }

  const form = new FormData(elements.verificationForm);
  const path = action === "link"
    ? "/account/external-logins/link"
    : "/account/external-logins/unlink";
  const method = action === "link" ? "PUT" : "DELETE";

  await run(async () => {
    const session = await request(path, {
      authenticated: true,
      body: { verificationCode: form.get("verificationCode") },
      csrf: true,
      method,
    });
    state.pendingMutation = null;
    elements.verificationForm.reset();
    elements.verificationPanel.hidden = true;
    await acceptSession(
      session,
      action === "link"
        ? "External login linked. The session was replaced."
        : "External login unlinked. The session was replaced.");
  });
}

function prepareOidcReturn(action) {
  closeOidcChannel();
  const channelId = crypto.randomUUID();
  state.oidcAction = action;
  state.oidcChannel = new BroadcastChannel(channelName(channelId));
  state.oidcChannel.addEventListener("message", event => {
    if (event.data?.type === "skopka-hello:oidc-return") {
      void finishExternalNavigation();
    }
  }, { once: true });
  return `${state.config.oidcReturnPath}?channel=${encodeURIComponent(channelId)}`;
}

function openProviderPopup(url) {
  const target = new URL(url, location.origin);
  if (target.origin !== location.origin) {
    throw new Error("The OIDC challenge URL must be same-origin.");
  }
  window.open(
    target.href,
    "_blank",
    "popup,noopener,width=520,height=720");
  setStatus("Waiting for the external provider…");
}

function closeOidcChannel() {
  state.oidcChannel?.close();
  state.oidcChannel = null;
  state.oidcAction = null;
}

function channelName(channelId) {
  return `skopka-hello-oidc:${channelId}`;
}

async function request(path, options = {}) {
  const headers = new Headers({ Accept: "application/json" });
  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
  }
  if (options.authenticated) {
    if (!state.accessToken) {
      throw new Error("An in-memory access token is required.");
    }
    headers.set("Authorization", `Bearer ${state.accessToken}`);
  }
  if (options.csrf) {
    const csrf = readCookie(state.config.antiforgeryCookieName);
    if (!csrf) {
      throw new Error("The antiforgery request cookie is missing.");
    }
    headers.set(state.config.antiforgeryHeaderName, csrf);
  }

  const response = await fetch(path, {
    body: options.body === undefined
      ? undefined
      : JSON.stringify(options.body),
    credentials: "same-origin",
    headers,
    method: options.method ?? "GET",
  });
  const contentType = response.headers.get("content-type") ?? "";
  const payload = contentType.includes("json")
    ? await response.json()
    : null;
  if (!response.ok) {
    throw new Error(
      payload?.detail
        ?? payload?.title
        ?? `Request failed with HTTP ${response.status}.`);
  }
  return payload;
}

function readCookie(name) {
  const prefix = `${encodeURIComponent(name)}=`;
  const entry = document.cookie
    .split("; ")
    .find(cookie => cookie.startsWith(prefix));
  return entry ? decodeURIComponent(entry.slice(prefix.length)) : null;
}

function emptyToNull(value) {
  const trimmed = String(value ?? "").trim();
  return trimmed.length === 0 ? null : trimmed;
}

async function run(operation) {
  try {
    await operation();
  } catch (error) {
    showError(error);
  }
}

function setStatus(message, error = false) {
  elements.status.textContent = message;
  elements.status.classList.toggle("error", error);
}

function showError(error) {
  setStatus(
    error instanceof Error ? error.message : "Unexpected client error.",
    true);
}
