# KommoAI Admin Panel

Panel de administración para el agente de IA multitenant conectado a Kommo.

## 🚀 Setup Rápido

### 1. Instalar dependencias

```bash
npm install
```

### 2. Configurar variables de entorno

Crea un archivo `.env` en la raíz del proyecto:

```env
VITE_API_URL=https://localhost:7000
VITE_ADMIN_API_KEY=tu-admin-key-aqui
```

### 3. Ejecutar en desarrollo

```bash
npm run dev
```

La aplicación estará disponible en `http://localhost:5173`

## 📦 Dependencias

```json
{
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "react-router-dom": "^6.22.0",
    "recharts": "^2.12.0",
    "lucide-react": "^0.263.1"
  },
  "devDependencies": {
    "@vitejs/plugin-react": "^4.2.1",
    "vite": "^5.1.0",
    "tailwindcss": "^3.4.1",
    "postcss": "^8.4.35",
    "autoprefixer": "^10.4.17"
  }
}
```

## 🏗️ Estructura del Proyecto

```
src/
├── components/
│   ├── Layout/
│   │   ├── Sidebar.jsx
│   │   └── Header.jsx
│   ├── Tenants/
│   │   ├── TenantList.jsx
│   │   ├── TenantForm.jsx
│   │   └── TenantDetail.jsx
│   ├── KnowledgeBase/
│   │   ├── KbList.jsx
│   │   └── KbUpload.jsx
│   ├── Metrics/
│   │   ├── MetricsSummary.jsx
│   │   ├── DailyUsage.jsx
│   │   └── CostManagement.jsx
│   └── Logs/
│       └── ErrorLogs.jsx
├── lib/
│   ├── api.js          # Cliente API
│   └── utils.js        # Utilidades
├── App.jsx
├── main.jsx
└── index.css
```

## 🎨 Features

- ✅ CRUD completo de Tenants
- ✅ Gestión de Knowledge Base por tenant
- ✅ Visualización de métricas y uso de IA
- ✅ Gestión de costos de IA
- ✅ Vista de logs y errores
- ✅ Diseño responsive y minimalista
- ✅ Gráficos interactivos

## 🔐 Seguridad

- La API Key se maneja mediante variable de entorno
- Nunca se almacena en localStorage
- Se envía en cada request mediante header `X-Admin-Key`

## 🚢 Deploy a Producción

### Variables de entorno en producción:

```env
VITE_API_URL=https://api.tudominio.com
VITE_ADMIN_API_KEY=tu-admin-key-produccion
```

### Build:

```bash
npm run build
```

Los archivos estáticos se generarán en la carpeta `dist/`

## 📝 Comandos

```bash
npm run dev      # Desarrollo
npm run build    # Build para producción
npm run preview  # Preview del build
```