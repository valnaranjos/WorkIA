# ---------- 1) Build del front (Vite) ----------
FROM node:20 AS webbuild
WORKDIR /src/admin-ui
COPY admin-ui/package*.json ./
RUN npm ci
COPY admin-ui/ ./
RUN npm run build
# genera: /src/admin-ui/dist

# ---------- 2) Build/publish del back ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiamos SOLO lo mínimo para cachear el restore
COPY KommoAIAgent/KommoAIAgent.csproj KommoAIAgent/
COPY KommoAIAgent.sln .
RUN dotnet restore KommoAIAgent/KommoAIAgent.csproj

# Ahora el resto del código
COPY KommoAIAgent/ KommoAIAgent/

# Copiamos el build del front dentro de wwwroot/admin
COPY --from=webbuild /src/admin-ui/dist/ KommoAIAgent/KommoAIAgent/wwwroot/admin/

# Publicamos
RUN dotnet publish KommoAIAgent/KommoAIAgent.csproj -c Release -o /app/out

# ---------- 3) Imagen final (runtime) ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/out ./

# En contenedor servimos HTTP; TLS lo pone el proxy
ENV ASPNETCORE_URLS=http://0.0.0.0:7000
EXPOSE 7000
ENTRYPOINT ["dotnet", "KommoAIAgent.dll"]
