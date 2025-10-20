import React, { useEffect, useMemo, useState } from "react";
import api from "../lib/api";
import { toast } from "../components/Toaster";
import Modal from "../components/Modal";

export default function CostsView() {
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);

  const [q, setQ] = useState("");
  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase();
    if (!s) return rows;
    return rows.filter(r =>
      (r.provider || "").toLowerCase().includes(s) ||
      (r.model || "").toLowerCase().includes(s)
    );
  }, [rows, q]);

  // modal create/edit
  const EMPTY = { provider: "", model: "", inputPer1K: "", outputPer1K: "", embPer1KTokens: "" };
  const [open, setOpen] = useState(false);
  const [editKey, setEditKey] = useState(null); // provider|model o null
  const [form, setForm] = useState(EMPTY);
  const [saving, setSaving] = useState(false);

  async function load() {
    setLoading(true);
    try {
      const data = await api.getCosts();
      setRows(Array.isArray(data) ? data : []);
    } catch (e) {
      toast("error", "No se pudieron cargar costos: " + (e?.message || e));
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => { load(); }, []);

  function openCreate() {
    setEditKey(null);
    setForm(EMPTY);
    setOpen(true);
  }
  function openEdit(r) {
    setEditKey(`${r.provider}|${r.model}`);
    setForm({
      provider: r.provider || "",
      model: r.model || "",
      inputPer1K: r.inputPer1K ?? "",
      outputPer1K: r.outputPer1K ?? "",
      embPer1KTokens: r.embPer1KTokens ?? "",
    });
    setOpen(true);
  }

  async function save() {
    // Validaciones mínimas
    if (!form.provider.trim()) return toast("error", "Provider es requerido");
    if (!form.model.trim()) return toast("error", "Model es requerido");

    const inV  = Number(form.inputPer1K ?? 0);
    const outV = Number(form.outputPer1K ?? 0);
    const embV = Number(form.embPer1KTokens ?? 0);
    if ([inV, outV, embV].some(v => Number.isNaN(v) || v < 0)) {
      return toast("error", "Los valores deben ser números ≥ 0");
    }

    setSaving(true);
    try {
      await api.saveCost({
        provider: form.provider.trim(),
        model: form.model.trim(),
        inputPer1K: inV,
        outputPer1K: outV,
        embPer1KTokens: embV,
      });
      toast("success", editKey ? "Costo actualizado" : "Costo creado");
      setOpen(false);
      await load();
    } catch (e) {
      toast("error", "Error al guardar: " + (e?.message || e));
    } finally {
      setSaving(false);
    }
  }

  async function remove(r) {
    if (!window.confirm(`Eliminar costo ${r.provider}/${r.model}?`)) return;
    try {
      await api.deleteCost(r.provider, r.model);
      toast("success", "Costo eliminado");
      await load();
    } catch (e) {
      toast("error", "No se pudo eliminar: " + (e?.message || e));
    }
  }

  return (
    <div className="p-4 space-y-4">
      <div className="flex items-center gap-3">
        <input
          value={q}
          onChange={e=>setQ(e.target.value)}
          placeholder="Buscar por provider o model…"
          className="border rounded px-3 py-2 w-80"
        />
        <button onClick={load} className="px-3 py-2 rounded border">Refrescar</button>
        <button onClick={openCreate} className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700 ml-auto">
          + Nuevo costo
        </button>
      </div>

      <div className="border rounded overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <Th>Provider</Th>
              <Th>Model</Th>
              <Th align="right">Input $ / 1K</Th>
              <Th align="right">Output $ / 1K</Th>
              <Th align="right">Embed $ / 1K</Th>
              <Th align="right">Acciones</Th>
            </tr>
          </thead>
          <tbody>
            {loading && <tr><td className="p-4" colSpan={6}>Cargando…</td></tr>}
            {!loading && filtered.length === 0 && <tr><td className="p-4 text-gray-500" colSpan={6}>Sin datos.</td></tr>}
            {filtered.map((r) => (
              <tr key={`${r.provider}|${r.model}`} className="border-t">
                <Td>{r.provider}</Td>
                <Td>{r.model}</Td>
                <Td align="right">${Number(r.inputPer1K || 0).toFixed(4)}</Td>
                <Td align="right">${Number(r.outputPer1K || 0).toFixed(4)}</Td>
                <Td align="right">${Number(r.embPer1KTokens || 0).toFixed(4)}</Td>
                <Td align="right">
                  <button className="text-indigo-600 hover:underline mr-3" onClick={()=>openEdit(r)}>Editar</button>
                  <button className="text-red-600 hover:underline" onClick={()=>remove(r)}>Eliminar</button>
                </Td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Modal
        title={editKey ? "Editar costo" : "Nuevo costo"}
        open={open}
        onClose={()=>!saving && setOpen(false)}
        footer={
          <>
            <button onClick={()=>setOpen(false)} disabled={saving} className="px-4 py-2 border rounded mr-2">Cancelar</button>
            <button onClick={save} disabled={saving} className="px-4 py-2 rounded bg-blue-600 text-white hover:bg-blue-700">
              {saving ? "Guardando…" : "Guardar"}
            </button>
          </>
        }
      >
        <div className="grid grid-cols-2 gap-3">
          <div className="col-span-1">
            <label className="text-xs text-gray-600">Provider*</label>
            <input
              value={form.provider}
              onChange={e=>setForm({ ...form, provider: e.target.value })}
              className="w-full border rounded px-3 py-2"
              placeholder="openai / azure / anthropic…"
              disabled={!!editKey}
            />
          </div>
          <div className="col-span-1">
            <label className="text-xs text-gray-600">Model*</label>
            <input
              value={form.model}
              onChange={e=>setForm({ ...form, model: e.target.value })}
              className="w-full border rounded px-3 py-2"
              placeholder="gpt-4o-mini / claude-3.5…"
              disabled={!!editKey}
            />
          </div>

          <div>
            <label className="text-xs text-gray-600">Input $ / 1K</label>
            <input
              type="number" step="0.0001" min="0"
              value={form.inputPer1K}
              onChange={e=>setForm({ ...form, inputPer1K: e.target.value })}
              className="w-full border rounded px-3 py-2"
            />
          </div>
          <div>
            <label className="text-xs text-gray-600">Output $ / 1K</label>
            <input
              type="number" step="0.0001" min="0"
              value={form.outputPer1K}
              onChange={e=>setForm({ ...form, outputPer1K: e.target.value })}
              className="w-full border rounded px-3 py-2"
            />
          </div>

          <div>
            <label className="text-xs text-gray-600">Embed $ / 1K</label>
            <input
              type="number" step="0.0001" min="0"
              value={form.embPer1KTokens}
              onChange={e=>setForm({ ...form, embPer1KTokens: e.target.value })}
              className="w-full border rounded px-3 py-2"
            />
          </div>
        </div>
      </Modal>
    </div>
  );
}

function Th({ children, align = "left" }) {
  return <th className={`p-3 text-${align}`}><span className="font-medium text-gray-700">{children}</span></th>;
}
function Td({ children, align = "left" }) {
  return <td className={`p-3 text-${align}`}>{children}</td>;
}
