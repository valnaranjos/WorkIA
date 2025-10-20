import React, { useState, useEffect, useCallback } from "react";
import api from "../lib/api";
import { toast } from "../components/Toaster";

/**
 * Dashboard - Vista principal con health checks y resumen de tenants
 */
export default function DashboardView() {
  const [loading, setLoading] = useState(false);
  const [tenants, setTenants] = useState([]);
  const [systemHealth, setSystemHealth] = useState(null);
  const [globalStats, setGlobalStats] = useState({
    totalTenants: 0,
    activeTenants: 0,
    totalCalls: 0,
    totalTokens: 0,
    totalCost: 0
  });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [tenantsData, liveHealth, readyHealth] = await Promise.all([
        api.getTenants(),
        api.getHealthLive().catch(() => ({ ok: false })),
        api.getHealthReady().catch(() => ({ ok: false }))
      ]);

      setTenants(tenantsData || []);
      setSystemHealth({
        live: liveHealth?.ok ?? false,
        ready: readyHealth?.ok ?? false
      });

      // Calcular stats globales
      const total = tenantsData?.length || 0;
      const active = tenantsData?.filter(t => t.isActive)?.length || 0;

      setGlobalStats({
        totalTenants: total,
        activeTenants: active,
        totalCalls: 0, // TODO: Necesitarías un endpoint agregado
        totalTokens: 0,
        totalCost: 0
      });
    } catch (err) {
      toast("error", `Error cargando dashboard: ${err.message}`);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Dashboard</h1>
        <button
          onClick={load}
          disabled={loading}
          className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? "Cargando..." : "Actualizar"}
        </button>
      </div>

      {/* System Health */}
      <div className="bg-white border rounded-lg p-4">
        <h2 className="font-semibold mb-3">Estado del Sistema</h2>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <div className="flex items-center gap-2">
            <div className={`w-3 h-3 rounded-full ${systemHealth?.live ? "bg-green-500" : "bg-red-500"}`} />
            <span className="text-sm">Proceso: {systemHealth?.live ? "OK" : "Error"}</span>
          </div>
          <div className="flex items-center gap-2">
            <div className={`w-3 h-3 rounded-full ${systemHealth?.ready ? "bg-green-500" : "bg-red-500"}`} />
            <span className="text-sm">DB/pgvector: {systemHealth?.ready ? "OK" : "Error"}</span>
          </div>
        </div>
      </div>

      {/* Global KPIs */}
      <div className="grid grid-cols-1 md:grid-cols-5 gap-4">
        <Kpi title="Total Tenants" value={globalStats.totalTenants} />
        <Kpi title="Tenants Activos" value={globalStats.activeTenants} color="green" />
        <Kpi title="Llamadas (30d)" value={globalStats.totalCalls.toLocaleString()} />
        <Kpi title="Tokens (30d)" value={globalStats.totalTokens.toLocaleString()} />
        <Kpi title="Costo (30d)" value={`$${globalStats.totalCost.toFixed(2)}`} />
      </div>

      {/* Tenants Table */}
      <div className="bg-white border rounded-lg overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b">
          <h2 className="font-semibold">Tenants</h2>
        </div>
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <Th>Slug</Th>
              <Th>Nombre</Th>
              <Th>Modelo IA</Th>
              <Th>Estado</Th>
              <Th align="right">Budget Mensual</Th>
            </tr>
          </thead>
          <tbody>
            {!loading && tenants.length === 0 && (
              <tr><td className="p-4 text-gray-500 text-center" colSpan={5}>Sin tenants</td></tr>
            )}
            {tenants.map(t => (
              <tr key={t.slug} className="border-t hover:bg-gray-50">
                <Td>{t.slug}</Td>
                <Td>{t.displayName}</Td>
                <Td>{t.iaModel}</Td>
                <Td>
                  <span className={`inline-flex px-2 py-1 rounded text-xs ${
                    t.isActive ? "bg-green-100 text-green-700" : "bg-gray-100 text-gray-600"
                  }`}>
                    {t.isActive ? "Activo" : "Inactivo"}
                  </span>
                </Td>
                <Td align="right">{(t.monthlyTokenBudget || 0).toLocaleString()} tokens</Td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/* =================== Componentes auxiliares =================== */

function Kpi({ title, value, color = "blue" }) {
  const colorClass = color === "red" ? "text-red-600" : 
                     color === "green" ? "text-green-600" : "text-blue-600";
  return (
    <div className="border rounded p-4 bg-white shadow-sm">
      <div className="text-xs text-gray-600 mb-1">{title}</div>
      <div className={`text-2xl font-semibold ${colorClass}`}>{value}</div>
    </div>
  );
}

function Th({ children, align = "left" }) {
  const alignClass = align === "right" ? "text-right" : 
                     align === "center" ? "text-center" : "text-left";
  return (
    <th className={`p-3 ${alignClass}`}>
      <span className="font-medium text-gray-700">{children}</span>
    </th>
  );
}

function Td({ children, align = "left" }) {
  const alignClass = align === "right" ? "text-right" : 
                     align === "center" ? "text-center" : "text-left";
  return <td className={`p-3 ${alignClass}`}>{children}</td>;
}