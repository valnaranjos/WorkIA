import React, { useEffect, useMemo, useState, useCallback } from "react";
import api from "../lib/api";
import { toast } from "../components/Toaster";

export default function LogsView() {
  const [tenants, setTenants] = useState([]);
  const [slug, setSlug] = useState("");
  const [size, setSize] = useState(20);

  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);

  // carga tenants para el selector
   useEffect(() => {
    (async () => {
      try {
        const list = await api.getTenants();
        setTenants(list || []);
        if ((list || []).length && !slug) setSlug(list[0].slug);
      } catch (err) {
        toast("error", `No se pudieron cargar tenants: ${err?.message || err}`);
      }
    })();
  }, []);

  // Cargar logs/errores
  const load = useCallback(async () => {
    if (!slug) return;
    setLoading(true);
    try {
      const data = await api.getLogs({ tenant: slug, limit: size });
      const items = Array.isArray(data) ? data : (data?.items || []);
      setRows(items);
    } catch (err) {
      toast("error", `No se pudieron cargar logs: ${err?.message || err}`);
    } finally {
      setLoading(false);
    }
  }, [slug, size]);

 useEffect(() => { 
    if (slug) load(); 
  }, [slug, size, load]);

return (
    <div className="p-4 space-y-4">
      {/* Filtros */}
      <div className="flex flex-wrap gap-3 items-center">
        <select 
          className="border rounded px-3 py-2" 
          value={slug} 
          onChange={(e) => setSlug(e.target.value)}
        >
          {tenants.map(t => (
            <option key={t.slug} value={t.slug}>
              {t.slug} — {t.displayName}
            </option>
          ))}
        </select>

        <select 
          className="border rounded px-3 py-2" 
          value={size} 
          onChange={(e) => setSize(Number(e.target.value))}
        >
          {[10, 20, 50, 100, 200].map(n => (
            <option key={n} value={n}>Mostrar {n} errores</option>
          ))}
        </select>

        <button
          onClick={load}
          disabled={loading || !slug}
          className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? "Cargando…" : "Actualizar"}
        </button>

        <div className="ml-auto text-sm text-gray-600">
          Total: {rows.length}
        </div>
      </div>

      {/* Tabla */}
      <div className="border rounded overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <Th>ID</Th>
              <Th>Fecha</Th>
              <Th>Provider</Th>
              <Th>Modelo</Th>
              <Th>Operación</Th>
              <Th>Mensaje</Th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr><td className="p-4" colSpan={6}>Cargando…</td></tr>
            )}
            {!loading && rows.length === 0 && (
              <tr><td className="p-4 text-gray-500" colSpan={6}>Sin errores registrados.</td></tr>
            )}
            {rows.map((r, idx) => (
              <tr key={r.id || idx} className="border-t">
                <Td>{r.id}</Td>
                <Td>{new Date(r.when_utc || Date.now()).toLocaleString()}</Td>
                <Td>{r.provider || "-"}</Td>
                <Td>{r.model || "-"}</Td>
                <Td className="font-mono text-xs">{r.operation || "-"}</Td>
                <Td className="max-w-md break-words">
                  <details className="cursor-pointer">
                    <summary className="text-red-600">
                      {(r.message || "").substring(0, 100)}
                      {(r.message || "").length > 100 && "..."}
                    </summary>
                    <pre className="mt-2 text-xs bg-gray-50 p-2 rounded overflow-auto max-h-40">
                      {r.message}
                    </pre>
                  </details>
                </Td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function Th({ children }) {
  return (
    <th className="p-3 text-left">
      <span className="font-medium text-gray-700">{children}</span>
    </th>
  );
}

function Td({ children, className = "" }) {
  return <td className={`p-3 align-top ${className}`}>{children}</td>;
}
