"use strict";

// Imitates the parts of Prusa-Link-Web (https://github.com/prusa3d/Prusa-Link-Web) that depend on the page address
// and on the login: src/auth.js (API_ROOT, setUpAuth, reaction to 401) and src/helpers/upload_request.js (XHR PUT).

// src/auth.js: the API lives under the path of the page (a proxy prefix such as /<token>/ is kept).
const API_ROOT = window.location.pathname.replace(/\/$/, "");

// The real web calls setUpAuth again after every 401 with a Digest challenge; this cap keeps a loop observable
// (e2e.setUpAuthCalls) instead of endless.
const MAX_SET_UP_AUTH_CALLS = 5;

const e2e = { setUpAuthCalls: 0, statusRefreshes: 0, ready: false };
window.e2e = e2e;

class AuthRequired extends Error {
  constructor(scheme) {
    super(`401 (${scheme})`);
    this.scheme = scheme;
  }
}

class ApiError extends Error {
  constructor(status, title, message) {
    super(`${title}: ${message}`);
    this.status = status;
  }
}

function show(id, text) {
  document.getElementById(id).textContent = text;
}

// src/errors.js handleError: firmware errors are JSON {"title": ..., "message": ...}.
function handleError(error) {
  show("error", error.message);
}

async function getJson(endPoint) {
  const response = await fetch(`${API_ROOT}${endPoint}`, { headers: { Accept: "application/json" } });
  if (response.status === 401) {
    // Like the real web: the scheme comes from the challenge; a 401 without WWW-Authenticate throws a TypeError here.
    const scheme = response.headers.get("WWW-Authenticate").split(" ")[0];
    sessionStorage.setItem("authType", scheme);
    throw new AuthRequired(scheme);
  }

  if (!response.ok) {
    const body = await response.json();
    throw new ApiError(response.status, body.title, body.message);
  }

  return response.json();
}

async function setUpAuth() {
  e2e.setUpAuthCalls++;
  try {
    const info = await getJson("/api/v1/info");
    show("auth", "logged-in");
    show("hostname", info.hostname);
    await refreshStatus();
  } catch (error) {
    if (error instanceof AuthRequired && error.scheme === "Digest" && e2e.setUpAuthCalls < MAX_SET_UP_AUTH_CALLS) {
      return setUpAuth();
    }

    show("auth", error instanceof AuthRequired ? `unauthenticated (${error.scheme})` : "failed");
    handleError(error);
  }
}

// The real web polls the status periodically; the tests call this explicitly to stay deterministic.
async function refreshStatus() {
  e2e.statusRefreshes++;
  try {
    const status = await getJson("/api/v1/status");
    show("state", status.printer.state);
    show("error", "");
    return "ok";
  } catch (error) {
    handleError(error);
    return error instanceof ApiError ? `error ${error.status}` : "error";
  }
}

window.refreshStatus = refreshStatus;

// src/helpers/upload_request.js: XMLHttpRequest PUT ${API_ROOT}${url} with Overwrite and Print-After-Upload.
// The body is generated (byte i = i mod 251), so the printer can check every byte without a file on disk.
window.uploadGenerated = (name, size) => new Promise((resolve) => {
  const data = new Uint8Array(size);
  for (let i = 0; i < size; i++) {
    data[i] = i % 251;
  }

  const request = new XMLHttpRequest();
  request.open("PUT", `${API_ROOT}/api/v1/files/usb/${encodeURIComponent(name)}`, true);
  request.setRequestHeader("Content-Type", "text/x.gcode");
  request.setRequestHeader("Overwrite", "?1");
  request.setRequestHeader("Print-After-Upload", "?1");
  request.onload = () => resolve(request.status);
  request.onerror = () => resolve(-1);
  request.send(new Blob([data]));
});

setUpAuth()
  .catch((error) => {
    // E.g. "Request cannot be constructed from a URL that includes credentials" (page opened as http://user:pass@host/).
    show("auth", "failed");
    handleError(error);
  })
  .finally(() => {
    e2e.ready = true;
  });
