import { createBrowserRouter } from 'react-router-dom'
import { AppLayout } from '../ui/AppLayout'
import { Home } from '../views/Home'
import { TenantsList } from '../views/tenants/TenantsList'
import { Metrics } from '../views/metrics/Metrics'
import { Logs } from '../views/logs/Logs'
import { Settings } from '../views/Settings'
import TenantForm from '../views/tenants/TenantForm'
import KbIngest from '../views/kb/KbIngest'
import KbDocDetail from '../views/kb/KbDocDetail'
import KbDocs from '../views/kb/KbDocs'

export const router = createBrowserRouter([
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <Home /> },
      { path: 'tenants', element: <TenantsList /> },
      { path: 'tenants/new', element: <TenantForm /> },         // ← crear
      { path: 'tenants/edit/:slug', element: <TenantForm /> },  // ← editar
      { path: 'kb/docs', element: <KbDocs /> },
      { path: 'metrics', element: <Metrics /> },
      { path: 'logs', element: <Logs /> },
      { path: 'settings', element: <Settings /> },
      { path: 'kb/ingest', element: <KbIngest /> },
{ path: 'kb/doc/:id', element: <KbDocDetail /> },
    ]
  }
])