// lib/api.js - Cliente para comunicación con el backend (CORREGIDO)

const API_URL = import.meta.env.PROD ? '' : '/api'; 

const API_KEY = import.meta.env.VITE_ADMIN_API_KEY;

class ApiClient {
  constructor() {
    this.baseUrl = API_URL;
    this.apiKey = API_KEY;
  }

  async request(endpoint, options = {}) {
    const url = `${this.baseUrl}${endpoint}`;
    const headers = {
      'X-Admin-Key': this.apiKey,
      ...(options.body && { 'Content-Type': 'application/json' }),
      ...(options.headers),
    };

    const res = await fetch(url, { ...options, headers });
    const ct = res.headers.get('content-type') || '';
    const data = ct.includes('application/json')
      ? await res.json().catch(() => null)
      : await res.text().catch(() => '');

    if (!res.ok) {
      throw new Error(typeof data === 'string' ? data : JSON.stringify(data));
    }
    return data;
  }

  // ========== TENANTS ==========
  getTenants() {
    return this.request('/admin/admintenants');
  }

  getTenant(slug) {
    return this.request(`/admin/admintenants/by-slug/${encodeURIComponent(slug)}`);
  }

  createTenant(data) {
    return this.request('/admin/admintenants', {
      method: 'POST',
      body: JSON.stringify(data),
    });
  }

  updateTenant(slug, data) {
    return this.request(`/admin/admintenants/by-slug/${encodeURIComponent(slug)}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    });
  }

  deleteTenant(slug) {
    return this.request(`/admin/admintenants/by-slug/${encodeURIComponent(slug)}`, {
      method: 'DELETE',
    });
  }

  // ========== PROMPT Y REGLAS ==========
  getPrompt(slug) {
    return this.request(`/admin/adminTenants/${encodeURIComponent(slug)}/prompt`);
  }

  updatePrompt(slug, systemPrompt) {
    return this.request(`/admin/adminTenants/${encodeURIComponent(slug)}/prompt`, {
      method: 'PUT',
      body: JSON.stringify({ systemPrompt }),
    });
  }

  getRules(slug) {
    return this.request(`/admin/admintenants/${encodeURIComponent(slug)}/rules`);
  }

  updateRules(slug, rules) {
    return this.request(`/admin/admintenants/${encodeURIComponent(slug)}/rules`, {
      method: 'PUT',
      body: JSON.stringify({ rules }),
    });
  }

  // ========== KB (CORREGIDO: tenant como query param) ==========
  getKbDocs({ tenant, q = "", tags = "", page = 1, pageSize = 20 }) {
    const qs = new URLSearchParams({
      tenant: tenant,
      ...(q && { q }),
      ...(tags && { tags }),
      page: String(page || 1),
      pageSize: String(pageSize || 20),
    }).toString();

    return this.request(`/admin/kb/docs?${qs}`);
  }

  // Lista chunks de un documento específico
  getKbChunks({ tenant, sourceId, take = 20 }) {
    const qs = new URLSearchParams({
      tenant: tenant,
      take: String(take || 20),
    }).toString();

    return this.request(`/admin/kb/doc/${encodeURIComponent(sourceId)}/chunks?${qs}`);
  }

  // Eliminar documento (usa sourceId, no id)
  kbDelete({ tenant, sourceId }) {
    return this.request(`/admin/kb/doc/${encodeURIComponent(sourceId)}?tenant=${encodeURIComponent(tenant)}`, {
      method: 'DELETE'
    });
  }

  // ========== INGESTA KB ==========
  ingestText(tenant, { title, text, tags }) {
    const sourceId = (title && title.trim())
      ? title.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
      : `text-${Date.now()}`;

    const body = {
      sourceId,
      title: title || undefined,
      content: text || '',
      tags: Array.isArray(tags)
        ? tags
        : (typeof tags === 'string'
          ? tags.split(',').map(s => s.trim()).filter(Boolean)
          : [])
    };

    return this.request(`/kb/ingest/text`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Tenant-Slug': tenant },
      body: JSON.stringify(body)
    });
  }

  ingestUrl(tenant, { url, tags }) {
    const tagsArray = Array.isArray(tags)
      ? tags
      : (typeof tags === 'string' ? tags.split(',').map(s => s.trim()).filter(Boolean) : []);

    return this.request(`/admin/kb/ingest/url?tenant=${encodeURIComponent(tenant)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Tenant-Slug': tenant },
      body: JSON.stringify({
        url,
        tags: tagsArray
      })
    });
  }

  ingestFile(tenant, file, { title, tags } = {}) {
    const fd = new FormData();
    fd.append('file', file);
    if (title) fd.append('title', title);
    if (tags) {
      const tagsArray = Array.isArray(tags)
        ? tags
        : (typeof tags === 'string' ? tags.split(',').map(s => s.trim()).filter(Boolean) : []);
      fd.append('tags', JSON.stringify(tagsArray));
    }

    return this.request(`/admin/kb/ingest/file?tenant=${encodeURIComponent(tenant)}`, {
      method: 'POST',
      headers: { 'X-Tenant-Slug': tenant },
      body: fd
    });
  }

  async ingestBatchText(items, { slug, concurrency = 3, onProgress } = {}) {
    const total = items.length || 0;
    let done = 0;
    const errors = [];

    const norm = items
      .map((it, idx) => {
        if (!it || !it.text) return null;
        const tags = Array.isArray(it.tags)
          ? it.tags
          : (typeof it.tags === 'string'
            ? it.tags.split(',').map(s => s.trim()).filter(Boolean)
            : []);
        return { idx, title: it.title || undefined, text: it.text, tags };
      })
      .filter(Boolean);

    let i = 0;
    const worker = async () => {
      while (i < norm.length) {
        const cur = norm[i++];
        try {
          await this.ingestText(slug, cur);
        } catch (e) {
          errors.push({ idx: cur.idx, error: e?.message || String(e) });
        } finally {
          done++;
          onProgress && onProgress(done, total);
        }
      }
    };

    const workers = Array.from(
      { length: Math.min(concurrency, norm.length || 1) },
      () => worker()
    );
    
    await Promise.all(workers);
    
    return { total, inserted: total - errors.length, errors };
  }

  // ========== MÉTRICAS ==========
  getMetricsSummary(tenant, from, to) {
    const query = new URLSearchParams({
      tenant,
      ...(from && { from: from.toISOString() }),
      ...(to && { to: to.toISOString() }),
    });
    return this.request(`/admin/metrics/summary?${query}`);
  }

  getDailyUsage(tenant, days = 7) {
    return this.request(`/admin/metrics/daily?tenant=${tenant}&days=${days}`);
  }

getLogs({ tenant, limit = 50 }) {
  const params = new URLSearchParams();
  if (tenant) params.set("tenant", tenant);
  params.set("limit", String(limit));
  
  return this.request(`/admin/metrics/errors?${params.toString()}`);
}

  // ========== COSTOS IA ==========
  getCosts() {
    return this.request('/admin/metrics/costs');
  }

  
// Crear/Actualizar (PUT upsert)
saveCost({ provider, model, inputPer1K, outputPer1K, embPer1KTokens }) {
  return this.request(`/admin/metrics/costs`, {
    method: "PUT",
    body: JSON.stringify({
      provider,
      model,
      inputPer1K: Number(inputPer1K ?? 0),
      outputPer1K: Number(outputPer1K ?? 0),
      embPer1KTokens: Number(embPer1KTokens ?? 0),
    }),
  });
}

  deleteCost(provider, model) {
  return this.request(
    `/admin/metrics/costs/${encodeURIComponent(provider)}/${encodeURIComponent(model)}`,
    { method: "DELETE" }
  );
}

  // ========== DIAGNOSTICS ==========
  getHealthLive() {
    return this.request('/health/live');
  }

  getHealthReady() {
    return this.request('/health/ready');
  }

  getTenantHealth(slug) {
    return this.request(`/t/${slug}/__health`);
  }

  getTenantBudget(slug) {
    return this.request(`/t/${slug}/__budget`);
  }

  getTenantWhoAmI(slug) {
    return this.request(`/t/${slug}/__whoami`);
  } 
 
}

export default new ApiClient();