import api from '../../lib/api/axios'

export type KbDoc = {
  id: string
  sourceId: string
  title: string
  tags?: string[] | null
  chunks?: number
}

export async function listDocs(tenant: string, page = 1, pageSize = 20, q?: string) {
  const params = new URLSearchParams({ tenant, page: String(page), pageSize: String(pageSize) })
  if (q && q.trim()) params.set('q', q.trim())
  const { data } = await api.get(`/admin/kb/docs?${params.toString()}`)
  return data as { items: KbDoc[]; total: number; page: number; pageSize: number }
}

export async function ingestText(
  tenant: string,
  payload: {
    sourceId: string
    title: string
    tags?: string[]
    text: string        // <- lo seguimos llamando text en el front...
  }
) {
  const params = new URLSearchParams({ tenant })
  // ...pero al backend le mandamos "content"
  const body = {
    sourceId: payload.sourceId,
    title: payload.title,
    content: payload.text,
    tags: payload.tags ?? null, // acepta null o string[]
  }

  const { data } = await api.post(`/kb/ingest/text?${params.toString()}`, body)
  return data
}

export async function search(tenant: string, q: string, page = 1, pageSize = 20) {
  const params = new URLSearchParams({ tenant, q, page: String(page), pageSize: String(pageSize) })
  const { data } = await api.get(`/kb/search?${params.toString()}`)
  return data as { items: Array<{ id: string; score: number; sourceId: string; title: string }> }
}

export async function getDocChunks(tenant: string, docId: string) {
  const { data } = await api.get(`/admin/kb/doc/${docId}/chunks?tenant=${encodeURIComponent(tenant)}`)
  return data as Array<{ idx: number; text: string }>
}
