import api from '../../lib/api/axios'

// ==== Tipos del formulario (UI) ====
export type TenantFormModel = {
  slug: string
  displayName: string
  isActive: boolean
  kommoBaseUrl: string
  kommoMensajeIaFieldId: number         // <-- requerido por el backend
  iaProvider: string
  iaModel: string
  monthlyTokenBudget: number
  alertThresholdPct: number             // UI en 0..100 (no fracción)
  systemPrompt: string
  businessRulesText?: string            // UI como texto JSON opcional
}

// ==== Create ====
type TenantCreateRequest = {
  slug?: string
  displayName: string
  kommoBaseUrl: string
  kommoAccessToken?: string | null
  kommoMensajeIaFieldId: number
  kommoScopeId?: string | null
  iaProvider: string
  iaModel: string
  maxTokens?: number
  temperature?: number
  topP?: number
  systemPrompt?: string | null
  businessRules?: any                   // JSON (object)
  debounceMs?: number
  ratePerMinute?: number
  ratePer5Minutes?: number
  memoryTTLMinutes?: number
  imageCacheTTLMinutes?: number
  monthlyTokenBudget: number
  alertThresholdPct: number             // 0..100
  isActive: boolean
}

export async function createTenantFromForm(f: TenantFormModel) {
  // parsear JSON si hay
  let businessRules: any | undefined = undefined
  if (f.businessRulesText && f.businessRulesText.trim().length > 0) {
    businessRules = JSON.parse(f.businessRulesText) // si falla lanzará error controlado
  }

  const req: TenantCreateRequest = {
    slug: f.slug || undefined,
    displayName: f.displayName,
    kommoBaseUrl: f.kommoBaseUrl,
    kommoMensajeIaFieldId: Number(f.kommoMensajeIaFieldId),
    iaProvider: f.iaProvider,
    iaModel: f.iaModel,
    systemPrompt: f.systemPrompt || undefined,
    businessRules,
    monthlyTokenBudget: Number(f.monthlyTokenBudget),
    alertThresholdPct: Number(f.alertThresholdPct), // 0..100
    isActive: !!f.isActive
  }

  const { data } = await api.post('/admin/admintenants', req)
  return data
}

// ==== Update ====
type TenantUpdateRequest = Partial<{
  displayName: string
  kommoBaseUrl: string
  kommoAccessToken: string
  kommoMensajeIaFieldId: number
  kommoScopeId: string
  iaProvider: string
  iaModel: string
  maxTokens: number
  temperature: number
  topP: number
  systemPrompt: string
  businessRules: any                    // JSON (object)
  debounceMs: number
  ratePerMinute: number
  ratePer5Minutes: number
  memoryTTLMinutes: number
  imageCacheTTLMinutes: number
  monthlyTokenBudget: number
  alertThresholdPct: number             // 0..100
  isActive: boolean
}>

export async function updateTenantFromForm(slug: string, f: TenantFormModel) {
  // Solo mandamos lo que tiene sentido (el backend aplica patch)
  const body: TenantUpdateRequest = {
    displayName: f.displayName,
    kommoBaseUrl: f.kommoBaseUrl,
    iaProvider: f.iaProvider,
    iaModel: f.iaModel,
    systemPrompt: f.systemPrompt,
    monthlyTokenBudget: Number(f.monthlyTokenBudget),
    alertThresholdPct: Number(f.alertThresholdPct),
    isActive: !!f.isActive,
  }

  if (f.kommoMensajeIaFieldId && f.kommoMensajeIaFieldId > 0) {
    body.kommoMensajeIaFieldId = Number(f.kommoMensajeIaFieldId)
  }

  if (f.businessRulesText && f.businessRulesText.trim().length > 0) {
    body.businessRules = JSON.parse(f.businessRulesText)
  }

  const { data } = await api.put(`/admin/admintenants/by-slug/${slug}`, body)
  return data
}

export async function activateTenant(slug: string) {
  // Mandar sólo isActive: true (y NO mandar BusinessRulesJson ni alert 0..1)
  const { data } = await api.put(`/admin/admintenants/by-slug/${slug}`, { isActive: true })
  return data
}

export async function listTenants() {
  const { data } = await api.get('/admin/admintenants')
  return data
}

export async function getTenant(slug: string) {
  const { data } = await api.get(`/admin/admintenants/by-slug/${slug}`)
  return data
}

export async function deleteTenant(slug: string) {
  await api.delete(`/admin/admintenants/by-slug/${slug}`)
}
