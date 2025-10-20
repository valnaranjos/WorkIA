import React, { useEffect, useMemo, useState, useCallback } from "react";
import api from "../lib/api";
import { toast } from "../components/Toaster";

export default function LogsView() {
  const [tenants, setTenants] = useState([]);
  const [slug, setSlug] = useState("");
  const [level, setLevel] = useState("");
  const [q, setQ] = useState("");
  const [days, setDays] = useState(7);

  const [page, setPage] = useState(1);
  const [size, setSize] = useState(20);

  const [rows, setRows] = useState([]);
  const [total, setTotal] = useState(0);
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const toRange = useCallback((n) => {
    const to = new Date();
    to.setHours(23, 59, 59, 999);
    const from = new Date();
    from.setDate(to.getDate() - n + 1);
    from.setHours(0, 0, 0, 0);
    return { from: from.toISOString(), to: to.toISOString() };
  }, []);

  const load = useCallback(async () => {
    if (!slug) return;
    setLoading(true);
    try {
      const { from, to } = toRange(days);
      const { items, total } = await api.getLogs({
        slug,
        level: level || undefined,
        q: q || undefined,
        from,
        to,
        page,
        size,
      });
      setRows(items || []);
      setTotal(Number(total || 0));
    } catch (err) {
      toast("error", `No se pudieron cargar logs: ${err?.message || err}`);
    } finally {
      setLoading(false);
    }
  }, [slug, level, q, days, page, size, toRange]);

  useEffect(() => { load(); }, [load]);

  const pages = useMemo(
    () => Math.max(1, Math.ceil((total || 0) / size)),
    [total, size]
  );

  return (
    <div className="p-4 space-y-4">
      {/* filtros */}
      <div className="flex flex-wrap gap-3 items-center">
        <select className="border rounded px-3 py-2" value={slug} onChange={(e)=>{ setSlug(e.target.value); setPage(1); }}>
          {tenants.map(t => <option key={t.slug} value={t.slug}>{t.slug} — {t.displayName}</option>)}
        </select>

        <select className="border rounded px-3 py-2" value={days} onChange={e=>{ setDays(Number(e.target.value)); setPage(1); }}>
          {[1,3,7,14,30].map(n => <option key={n} value={n}>{n} días</option>)}
        </select>

        <select className="border rounded px-3 py-2" value={level} onChange={e=>{ setLevel(e.target.value); setPage(1); }}>
          <option value="">Todos los niveles</option>
          <option value="debug">debug</option>
          <option value="info">info</option>
          <option value="warn">warn</option>
          <option value="error">error</option>
        </select>

        <input
          className="border rounded px-3 py-2 w-64"
          placeholder="Buscar en mensaje/ctx…"
          value={q}
          onChange={e=>{ setQ(e.target.value); setPage(1); }}
        />

        <button
          onClick={load}
          disabled={loading || !slug}
          className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? "Cargando…" : "Buscar"}
        </button>
      </div>

      {/* tabla */}
      <div className="border rounded overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <Th>Fecha</Th>
              <Th>Nivel</Th>
              <Th>Tenant</Th>
              <Th>Mensaje</Th>
              <Th>Origen</Th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr><td className="p-4" colSpan={5}>Cargando…</td></tr>
            )}
            {!loading && rows.length===0 && (
              <tr><td className="p-4 text-gray-500" colSpan={5}>Sin resultados.</td></tr>
            )}
            {rows.map(r => (
              <tr key={r.id || `${r.timestamp}-${r.message}`} className="border-t">
                <Td>{new Date(r.timestamp || r.time || Date.now()).toLocaleString()}</Td>
                <Td>
                  <span className={
                    r.level === "error" ? "text-red-600" :
                    r.level === "warn"  ? "text-yellow-700" :
                    r.level === "debug" ? "text-gray-500" : "text-slate-700"
                  }>{r.level || "info"}</span>
                </Td>
                <Td>{r.tenant || slug}</Td>
                <Td className="whitespace-pre-wrap break-words">{r.message}</Td>
                <Td>{r.source || r.category || "-"}</Td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* paginación */}
      <div className="flex items-center gap-2">
        <button
          className="px-3 py-1 border rounded"
          onClick={()=> setPage(p => Math.max(1, p-1))}
          disabled={page<=1}
        >«</button>
        <div className="text-sm">Página {page} / {pages}</div>
        <button
          className="px-3 py-1 border rounded"
          onClick={()=> setPage(p => Math.min(pages, p+1))}
          disabled={page>=pages}
        >»</button>

        <select className="ml-2 border rounded px-2 py-1" value={size} onChange={e=>{ setSize(Number(e.target.value)); setPage(1); }}>
          {[10,20,50,100].map(n => <option key={n} value={n}>{n}/pág</option>)}
        </select>
      </div>
    </div>
  );
}

function Th({ children }) {
  return <th className="p-3 text-left"><span className="font-medium text-gray-700">{children}</span></th>;
}
function Td({ children }) {
  return <td className="p-3 align-top">{children}</td>;
}
