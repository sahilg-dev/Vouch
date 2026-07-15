const BASE = import.meta.env.VITE_API_URL || "http://localhost:5080";
const TOKEN_KEY = "vouch.token";

export function getToken() {
  return localStorage.getItem(TOKEN_KEY);
}

function setToken(token) {
  if (token) localStorage.setItem(TOKEN_KEY, token);
  else localStorage.removeItem(TOKEN_KEY);
}

function authHeaders() {
  const token = getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function req(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { "Content-Type": "application/json", ...authHeaders() },
    ...options,
  });
  if (res.status === 401) setToken(null);
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Request failed: ${res.status}`);
  }
  return res.status === 204 ? null : res.json();
}

export const api = {
  // ---- auth ----
  signup: (email, password) =>
    req("/api/auth/signup", { method: "POST", body: JSON.stringify({ email, password }) }).then((r) => {
      setToken(r.token);
      return r;
    }),
  login: (email, password) =>
    req("/api/auth/login", { method: "POST", body: JSON.stringify({ email, password }) }).then((r) => {
      setToken(r.token);
      return r;
    }),
  me: () => req("/api/auth/me"),
  logout: () => setToken(null),

  // ---- candidates ----
  createCandidate: (body) =>
    req("/api/candidates", { method: "POST", body: JSON.stringify(body) }),
  getCandidate: (id) => req(`/api/candidates/${id}`),
  ingest: (body) => req("/api/ingest", { method: "POST", body: JSON.stringify(body) }),
  createJob: (body) => req("/api/jobs", { method: "POST", body: JSON.stringify(body) }),
  matches: (id, sort = "recent") => req(`/api/candidates/${id}/matches?sort=${sort}`),
  tailor: (candidateId, jobId) =>
    req("/api/tailor", { method: "POST", body: JSON.stringify({ candidateId, jobId }) }),
  tailoredVersions: (candidateId, jobId) =>
    req(`/api/candidates/${candidateId}/jobs/${jobId}/tailored-versions`),
  prefill: (candidateId, jobId) =>
    req("/api/prefill", { method: "POST", body: JSON.stringify({ candidateId, jobId }) }),
  createApplication: (body) =>
    req("/api/applications", { method: "POST", body: JSON.stringify(body) }),
  applications: (id) => req(`/api/candidates/${id}/applications`),
  updateApplication: (id, body) =>
    req(`/api/applications/${id}`, { method: "PATCH", body: JSON.stringify(body) }),
  insights: (id) => req(`/api/candidates/${id}/insights`),

  // ---- resume export ----
  async downloadTailored(id, format, fileName) {
    const res = await fetch(`${BASE}/api/tailored/${id}/export?format=${format}`, {
      headers: authHeaders(),
    });
    if (!res.ok) throw new Error(`Export failed: ${res.status}`);
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName || `resume.${format}`;
    a.click();
    URL.revokeObjectURL(url);
  },
};
