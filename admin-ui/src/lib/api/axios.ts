import Axios from 'axios'

const api = Axios.create({
  baseURL: import.meta.env.VITE_API_BASE || '/api'
})

api.interceptors.request.use((config) => {
  const apiKey = localStorage.getItem('ADMIN_API_KEY') || import.meta.env.VITE_ADMIN_API_KEY
  const tenant = localStorage.getItem('TENANT_SLUG') || import.meta.env.VITE_DEFAULT_TENANT
  if (apiKey) config.headers['X-Admin-Key'] = apiKey
  if (tenant) config.headers['X-Tenant-Slug'] = tenant
  return config
})

export default api