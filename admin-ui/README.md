# 🎨 KommoAI Admin UI

Panel de administración para KommoAI Agent - Gestión de tenants, knowledge base y métricas.

## 🚀 Tech Stack

- **React 18** - UI Library
- **Vite** - Build tool & dev server
- **Tailwind CSS** - Styling
- **Fetch API** - HTTP client

## 📋 Requisitos

- Node.js 18+ o 20+
- npm 9+ o yarn
- Backend corriendo en `https://localhost:7000`

## 🛠️ Instalación

```bash
# 1. Instalar dependencias
npm install

# 2. Configurar variables de entorno
cp .env.example .env
# Editar .env y configurar VITE_ADMIN_API_KEY

# 3. Ejecutar en desarrollo
npm run dev
```

El admin UI estará disponible en: `http://localhost:5174`

## 🏗️ Build para producción

```bash
# Generar build optimizado
npm run build

# El output estará en ./dist/
# Puedes servir con:
npm run preview
```

## 📁 Estructura

```
admin-ui/
├── src/
│   ├── components/      # Componentes reutilizables (Modal, Toaster)
│   ├── views/          # Vistas principales (Tenants, KB, Metrics, etc)
│   ├── lib/
│   │   └── api.js      # Cliente HTTP para backend
│   ├── App.jsx         # Componente raíz con navegación
│   ├── main.jsx        # Entry point
│   └── index.css       # Tailwind imports
├── index.html
├── vite.config.js
├── tailwind.config.js
└── package.json
```

## 🔐 Configuración

### Variables de entorno

Crea `.env` con:

```bash
# API Backend (vacío en dev, URL completa en prod)
VITE_API_URL=

# Admin API Key (debe coincidir con el backend)
VITE_ADMIN_API_KEY=tu-api-key-aqui
```

### Proxy en desarrollo

El `vite.config.js` ya tiene configurado un proxy hacia el backend local:

```javascript
proxy: {
  '/admin': { target: 'https://localhost:7000' },
  '/kb': { target: 'https://localhost:7000' },
  '/t': { target: 'https://localhost:7000' },
  '/health': { target: 'https://localhost:7000' },
}
```

## 🧪 Testing local

1. Backend corriendo: `dotnet run` (puerto 7000)
2. Frontend corriendo: `npm run dev` (puerto 5174)
3. Abrir: `http://localhost:5174`
4. Login con tu `VITE_ADMIN_API_KEY`

## 📦 Deployment

### Opción A: Servir desde .NET (wwwroot)

```bash
# 1. Build del frontend
npm run build

# 2. Copiar dist/ a wwwroot del backend
cp -r dist/* ../KommoAIAgent/wwwroot/admin/

# 3. El backend servirá el admin en /admin
```

### Opción B: S3 + CloudFront (separado)

```bash
# 1. Build con URL de producción
VITE_API_URL=https://api.tudominio.com npm run build

# 2. Deploy a S3
aws s3 sync dist/ s3://your-bucket/admin/ --delete

# 3. Invalidar CloudFront
aws cloudfront create-invalidation --distribution-id XYZ --paths "/admin/*"
```

## 🐛 Troubleshooting

### Error: "Failed to fetch"
- Verifica que el backend esté corriendo
- Verifica que `VITE_ADMIN_API_KEY` sea correcta
- Revisa la consola del navegador

### Error: 401 Unauthorized
- Tu `VITE_ADMIN_API_KEY` no coincide con la del backend
- Verifica `Admin:ApiKey` en `appsettings.json`

### Tailwind no funciona
```bash
rm -rf node_modules dist
npm install
npm run dev
```

## 📚 Recursos

- [React Docs](https://react.dev)
- [Vite Docs](https://vitejs.dev)
- [Tailwind CSS Docs](https://tailwindcss.com)