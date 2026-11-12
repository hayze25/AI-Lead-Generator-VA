// Thin client for the ASP.NET Core API. Set VITE_API_URL if the API is not served from the same origin.
const BASE = import.meta.env.VITE_API_URL ?? "";

// The login token lives in localStorage so a refresh doesn't sign you out. Storage can be blocked
// (private windows etc.), so every access is wrapped and the app still works for the current tab.
const TOKEN_KEY = "biz_token";
let memoryToken = null;

export const auth = {
  get token() {
    try {
      return localStorage.getItem(TOKEN_KEY) ?? memoryToken;
    } catch {
      return memoryToken;
    }
  },
  set(token) {
    memoryToken = token;
    try {
      token ? localStorage.setItem(TOKEN_KEY, token) : localStorage.removeItem(TOKEN_KEY);
    } catch {
      /* storage unavailable */
    }
  },
  onUnauthorized: null, // App sets this to show the login screen when the token is rejected/expired
};

async function request(path, options = {}) {
  const headers = { "Content-Type": "application/json" };
  if (auth.token) headers.Authorization = `Bearer ${auth.token}`;

  let res;
  try {
    res = await fetch(`${BASE}/api${path}`, { ...options, headers });
  } catch {
    throw new Error("Can't reach the server. Is the API running?");
  }

  if (res.status === 401 && !path.startsWith("/auth/")) {
    auth.set(null);
    auth.onUnauthorized?.();
    throw new Error("Your session has expired. Please sign in again.");
  }

  if (!res.ok) {
    let message = `Request failed (${res.status})`;
    try {
      const body = await res.json();
      if (body.error) message = body.error;
      else if (body.errors) message = Object.values(body.errors).flat()[0] ?? message; // validation problems
      else if (body.title) message = body.title;
    } catch {
      /* response had no JSON body */
    }
    throw new Error(message);
  }
  return res.status === 204 ? null : res.json();
}

const json = (method, body) => ({ method, body: JSON.stringify(body ?? {}) });

const query = (params = {}) => {
  const q = new URLSearchParams(Object.entries(params).filter(([, v]) => v !== "" && v != null));
  return q.toString() ? `?${q}` : "";
};

export const api = {
  login: (username, password) => request("/auth/login", json("POST", { username, password })),
  register: (businessName, email, password) => request("/auth/register", json("POST", { businessName, email, password })),
  branding: () => request("/branding"),

  stats: () => request("/stats"),

  inquiries: (params) => request(`/inquiries${query(params)}`),
  inquiry: (id) => request(`/inquiries/${id}`),
  createInquiry: (body) => request("/inquiries", json("POST", body)),
  retriage: (id) => request(`/inquiries/${id}/triage`, { method: "POST" }),
  setInquiryStatus: (id, status) => request(`/inquiries/${id}/status`, json("PATCH", { status })),
  draftReply: (id, instruction) => request(`/inquiries/${id}/replies/draft`, json("POST", { instruction })),

  updateReply: (id, body) => request(`/replies/${id}`, json("PUT", { body })),
  sendReply: (id) => request(`/replies/${id}/send`, { method: "POST" }),

  leads: (params) => request(`/leads${query(params)}`),
  lead: (id) => request(`/leads/${id}`),
  updateLead: (id, changes) => request(`/leads/${id}`, json("PATCH", changes)),
  draftFollowUp: (id, inDays) => request(`/leads/${id}/followups/draft`, json("POST", { inDays })),
  cancelFollowUp: (leadId, followUpId) =>
    request(`/leads/${leadId}/followups/${followUpId}/cancel`, { method: "POST" }),

  settings: () => request("/settings"),
  saveSettings: (body) => request("/settings", json("PUT", body)),
};
