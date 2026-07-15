const BASE = import.meta.env.VITE_API_URL || "http://localhost:5080";

async function req(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Request failed: ${res.status}`);
  }
  return res.status === 204 ? null : res.json();
}

export const api = {
  createCandidate: (body) =>
    req("/api/candidates", { method: "POST", body: JSON.stringify(body) }),
  getCandidate: (id) => req(`/api/candidates/${id}`),
  ingest: (body) => req("/api/ingest", { method: "POST", body: JSON.stringify(body) }),
  createJob: (body) => req("/api/jobs", { method: "POST", body: JSON.stringify(body) }),
  matches: (id, sort = "recent") => req(`/api/candidates/${id}/matches?sort=${sort}`),
  tailor: (candidateId, jobId) =>
    req("/api/tailor", { method: "POST", body: JSON.stringify({ candidateId, jobId }) }),
  prefill: (candidateId, jobId) =>
    req("/api/prefill", { method: "POST", body: JSON.stringify({ candidateId, jobId }) }),
  createApplication: (body) =>
    req("/api/applications", { method: "POST", body: JSON.stringify(body) }),
  applications: (id) => req(`/api/candidates/${id}/applications`),
  updateApplication: (id, body) =>
    req(`/api/applications/${id}`, { method: "PATCH", body: JSON.stringify(body) }),
  insights: (id) => req(`/api/candidates/${id}/insights`),
};
