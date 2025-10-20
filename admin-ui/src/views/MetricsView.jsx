import React, { useEffect, useMemo, useState, useCallback } from "react";
import api from "../lib/api";
import { toast } from "../components/Toaster";

/**
 * Métricas por tenant (CORREGIDO)
 * - KPIs (llamadas, tokens IN/OUT, errores, costo)
 * - Sparkline "Tokens OUT por día"
 * - Tabla "Uso por modelo" + costo por fila
 * - Carga automática inicial
 * - Clases de Tailwind corregidas (sin interpolación dinámica)
 */

export default function MetricsView() {
  const [tenants, setTenants] = useState([]);
  const [slug, setSlug] = useState("");
  const [days, setDays] = useState(30);
  const [loading, setLoading] = useState(false);

  const [kpis, setKpis] = useState({ calls: 0, in: 0, out: 0, errors: 0, cost: 0 });
  const [usageRows, setUsageRows] = useState([]);
  const [tokensSeries, setTokensSeries] = useState([]);

  // ==== helpers ====
  const toISODateRange = useCallback((nDays) => {
    const to = new Date();
    to.setUTCHours(0, 0, 0, 0);
    const from = new Date(to);
    from.setUTCDate(to.getUTCDate() - nDays);
    return { from, to };
  }, []);

  const priceKey = (p, m) => `${String(p || "").toLowerCase()}|${String(m || "").toLowerCase()}`;

  // ==== cargar lista de tenants ====
  useEffect(() => {
    let isMounted = true;
    (async () => {
      try {
        const list = await api.getTenants();
        if (!isMounted) return;
        setTenants(list || []);
        if ((list || []).length && !slug) setSlug(list[0].slug);
      } catch (err) {
        toast("error", `No se pudo cargar tenants: ${err?.message || err}`);
      }
    })();
    return () => {
      isMounted = false;
    };
  }, [slug]);

  // ==== cargar métricas ====
  const load = useCallback(async () => {
    if (!slug) return;
    setLoading(true);
    try {
      const { from, to } = toISODateRange(days);

      const [summary, daily, costs] = await Promise.all([
        api.getMetricsSummary(slug, from, to),
        api.getDailyUsage(slug, days),
        api.getCosts(),
      ]);

      const summaryItems = Array.isArray(summary) ? summary : (summary?.items || []);
      const dailyItems = Array.isArray(daily) ? daily : (daily?.items || []);
      const costRows = Array.isArray(costs) ? costs : [];

      // mapa de precios
      const priceMap = new Map();
      for (const c of costRows) {
        priceMap.set(priceKey(c.provider, c.model), {
          in1k: Number(c.inputPer1K ?? 0),
          out1k: Number(c.outputPer1K ?? 0),
          emb1k: Number(c.embPer1KTokens ?? 0),
        });
      }

      // Uso por modelo + costo por fila
      let callsTot = 0;
      let inTot = 0;
      let outTot = 0;
      let errTot = 0;
      let costTot = 0;

      const rows = summaryItems.map((r, idx) => {
        const provider = r.provider;
        const model = r.model;
        const tokensIn = Number(r.input_tokens || 0);
        const tokensOut = Number(r.output_tokens || 0);
        const embeddingChars = Number(r.embedding_chars || 0);
        const calls = Number(r.calls || 0);
        const errors = Number(r.errors || 0);

        const price = priceMap.get(priceKey(provider, model)) || { in1k: 0, out1k: 0, emb1k: 0 };
        const costUsd =
          (tokensIn / 1000) * price.in1k +
          (tokensOut / 1000) * price.out1k +
          (embeddingChars / 1000) * price.emb1k;

        callsTot += calls;
        inTot += tokensIn;
        outTot += tokensOut;
        errTot += errors;
        costTot += costUsd;

        return {
          id: `${provider}|${model}|${idx}`,
          provider,
          model,
          calls,
          tokensIn,
          tokensOut,
          embeddingChars,
          errors,
          costUsd,
        };
      });

      // Serie OUT por día
      const dayMap = new Map();
      for (const d of dailyItems) {
        const out = Number(d.output_tokens || 0);
        const day = d.day;
        dayMap.set(day, (dayMap.get(day) || 0) + out);
      }
      const series = Array.from(dayMap.entries())
        .sort((a, b) => a[0].localeCompare(b[0]))
        .map(([date, out]) => ({ date, out }));

      setUsageRows(rows);
      setTokensSeries(series);
      setKpis({ calls: callsTot, in: inTot, out: outTot, errors: errTot, cost: costTot });
    } catch (err) {
      toast("error", `No se pudieron cargar métricas: ${err?.message || err}`);
    } finally {
      setLoading(false);
    }
  }, [slug, days, toISODateRange]);

  // CORREGIDO: Carga automática cuando cambia slug o days
  useEffect(() => {
    if (slug) {
      load();
    }
  }, [slug, days, load]);

  return (
    <div className="p-4 space-y-4">
      {/* Filtros */}
      <div className="flex flex-wrap items-center gap-3">
        <select
          value={slug}
          onChange={(e) => setSlug(e.target.value)}
          className="border rounded px-3 py-2"
          aria-label="Seleccionar tenant"
        >
          {tenants.map((t) => (
            <option key={t.slug} value={t.slug}>
              {t.slug} — {t.displayName}
            </option>
          ))}
        </select>

        <select
          value={days}
          onChange={(e) => setDays(Number(e.target.value))}
          className="border rounded px-3 py-2"
          aria-label="Rango de días"
        >
          {[7, 14, 30, 60, 90].map((n) => (
            <option key={n} value={n}>
              {n} días
            </option>
          ))}
        </select>

        <button
          type="button"
          onClick={load}
          disabled={loading || !slug}
          className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? "Cargando…" : "Cargar métricas"}
        </button>
      </div>

      {/* KPIs */}
      <div className="grid grid-cols-1 md:grid-cols-5 gap-4">
        <Kpi title="Llamadas" value={kpis.calls} />
        <Kpi title="Tokens IN" value={kpis.in.toLocaleString()} />
        <Kpi title="Tokens OUT" value={kpis.out.toLocaleString()} />
        <Kpi title="Errores" value={kpis.errors} />
        <Kpi title="Costo estimado" value={`$${kpis.cost.toFixed(2)}`} />
      </div>

      {/* Sparkline */}
      <div className="border rounded p-4">
        <div className="text-sm font-medium mb-2">Tokens OUT por día</div>
        <Spark data={tokensSeries} />
      </div>

      {/* Tabla uso por modelo */}
      <div className="border rounded overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <Th>Provider</Th>
              <Th>Modelo</Th>
              <Th align="right">Llamadas</Th>
              <Th align="right">Tokens IN</Th>
              <Th align="right">Tokens OUT</Th>
              <Th align="right">Emb. chars</Th>
              <Th align="right">Errores</Th>
              <Th align="right">Costo $</Th>
            </tr>
          </thead>
          <tbody>
            {!loading && usageRows.length === 0 && (
              <tr>
                <td className="p-4 text-gray-500" colSpan={8}>
                  Sin datos.
                </td>
              </tr>
            )}
            {usageRows.map((r) => (
              <tr key={r.id} className="border-t">
                <Td>{r.provider}</Td>
                <Td>{r.model}</Td>
                <Td align="right">{r.calls}</Td>
                <Td align="right">{r.tokensIn.toLocaleString()}</Td>
                <Td align="right">{r.tokensOut.toLocaleString()}</Td>
                <Td align="right">{r.embeddingChars.toLocaleString()}</Td>
                <Td align="right">{r.errors}</Td>
                <Td align="right">${r.costUsd.toFixed(2)}</Td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/* =================== Subcomponentes =================== */

function Kpi({ title, value }) {
  return (
    <div className="border rounded p-4">
      <div className="text-xs text-gray-600">{title}</div>
      <div className="text-2xl font-semibold">{value}</div>
    </div>
  );
}

// CORREGIDO: Clases completas en lugar de interpolación dinámica
function Th({ children, align = "left" }) {
  const alignClass = align === "right" ? "text-right" : align === "center" ? "text-center" : "text-left";
  return (
    <th className={`p-3 ${alignClass}`}>
      <span className="font-medium text-gray-700">{children}</span>
    </th>
  );
}

function Td({ children, align = "left" }) {
  const alignClass = align === "right" ? "text-right" : align === "center" ? "text-center" : "text-left";
  return <td className={`p-3 ${alignClass}`}>{children}</td>;
}

function Spark({ data }) {
  const values = useMemo(() => (Array.isArray(data) ? data.map((d) => Number(d.out || 0)) : []), [data]);
  if (!values.length) return <div className="text-gray-400 text-sm">Sin datos</div>;

  const max = Math.max(...values) || 1;
  return (
    <div className="h-28 w-full flex items-end gap-1">
      {values.map((v, i) => (
        <div
          key={`spark-${i}`}
          className="bg-blue-500 flex-1 rounded-sm"
          style={{ height: `${(v / max) * 100}%` }}
          aria-label={`Día ${i + 1}, tokens OUT ${v}`}
          title={`${v.toLocaleString()} tokens`}
        />
      ))}
    </div>
  );
}