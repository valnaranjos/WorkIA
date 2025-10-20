// src/views/TenantsView.jsx
import React, { useEffect, useMemo, useRef, useState } from "react";
import api from "../lib/api";
import Modal from "../components/Modal";
import { toast } from "../components/Toaster";

/**
 * Vista de administración de Tenants
 * - Lista + búsqueda con debounce
 * - Crear/Editar en modal con tabs
 * - Validaciones y normalización de payload
 * - Manejo de errores del back más legible
 */
export default function TenantsView() {
  // ---- Estado base ----
  const [tenants, setTenants] = useState([]);
  const [loading, setLoading] = useState(false);

  // ---- Modal / edición ----
  const [open, setOpen] = useState(false);
  const [edit, setEdit] = useState(null);   // objeto tenant | null
  const [tab, setTab] = useState("basic");  // basic | ia | kommo | br
  const [saving, setSaving] = useState(false);

  // Form inicial para "Nuevo"
  const EMPTY_FORM = {
    slug: "",
    displayName: "",
    kommoBaseUrl: "",
    isActive: true,

    iaProvider: "openai",
    iaModel: "gpt-4o-mini",
    monthlyTokenBudget: 5_000_000,
    temperature: 0.2,
    systemPrompt: "",

    kommoAccessToken: "",
    kommoMensajeIaFieldId: "",
    kommoScopeId: "",

    businessRulesJson: ""
  };
  const [form, setForm] = useState(EMPTY_FORM);

  // Para enfocar el primer input al abrir modal de "Nuevo"
  const slugRef = useRef(null);
  useEffect(() => { if (open && !edit) slugRef.current?.focus(); }, [open, edit]);

  // =======================
  // CARGA INICIAL
  // =======================
  async function load() {
    try {
      setLoading(true);
      const list = await api.getTenants(); // GET /admin/admintenants
      setTenants(list || []);
    } catch (e) {
      toast("error", "No se pudieron cargar tenants");
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => { load(); }, []);

  // =======================
  // BÚSQUEDA (debounce)
  // =======================
  const [search, setSearch] = useState("");
  const [q, setQ] = useState("");
  useEffect(() => {
    const t = setTimeout(() => setQ(search.trim().toLowerCase()), 250);
    return () => clearTimeout(t);
  }, [search]);

  const filtered = useMemo(() => {
    if (!q) return tenants;
    return tenants.filter(t =>
      (t.slug || "").toLowerCase().includes(q) ||
      (t.displayName || "").toLowerCase().includes(q)
    );
  }, [tenants, q]);

  // =======================
  // ABRIR CREAR / EDITAR
  // =======================
  function openCreate() {
    setEdit(null);
    setForm(EMPTY_FORM);
    setTab("basic");
    setOpen(true);
  }

  // (6) abrir “Editar” trayendo datos del back
  async function openEdit(row) {
    try {
      const full = await api.getTenant(row.slug); // GET /admin/admintenants/{slug}
      setEdit(full);
      setForm({
        ...EMPTY_FORM,
        ...full,
        // asegurar tipos controlados en inputs
        monthlyTokenBudget: Number(full.monthlyTokenBudget ?? 0),
        temperature: Number(full.temperature ?? 0),
        kommoMensajeIaFieldId:
          full.kommoMensajeIaFieldId == null ? "" : String(full.kommoMensajeIaFieldId),
        businessRulesJson: full.businessRulesJson ?? ""
      });
    } catch {
      // Fallback si el detalle falla: usar lo que hay en la fila
      setEdit(row);
      setForm({
        ...EMPTY_FORM,
        ...row,
        monthlyTokenBudget: Number(row.monthlyTokenBudget ?? 0),
        temperature: Number(row.temperature ?? 0),
        kommoMensajeIaFieldId:
          row.kommoMensajeIaFieldId == null ? "" : String(row.kommoMensajeIaFieldId),
        businessRulesJson: row.businessRulesJson ?? ""
      });
    }
    setTab("basic");
    setOpen(true);
  }

  // =======================
  // HELPERS
  // =======================
  // (5) Parsear errores del back en formato ProblemDetails + errors{...}
  function parseApiError(err) {
    try {
      const data = JSON.parse(err?.message || "");
      if (data?.errors && typeof data.errors === "object") {
        const first = Object.values(data.errors).flat()[0];
        return first || data.title || "Error de validación";
      }
      return data?.title || data?.detail || "Error inesperado";
    } catch {
      return err?.message || "Error inesperado";
    }
  }

  // Normalizar payload para el back (números y nulls)
  function buildPayload() {
    // Validación suave de JSON (si hay texto)
    let businessRulesJson = null;
    if (form.businessRulesJson && String(form.businessRulesJson).trim() !== "") {
      try {
        JSON.parse(form.businessRulesJson);
        businessRulesJson = form.businessRulesJson; // el back espera string JSON
      } catch {
        throw new Error("Business Rules no es un JSON válido");
      }
    }

    const kommoFieldId =
      form.kommoMensajeIaFieldId === null ||
      form.kommoMensajeIaFieldId === undefined ||
      String(form.kommoMensajeIaFieldId).trim() === ""
        ? null
        : Number(form.kommoMensajeIaFieldId);

    if (kommoFieldId !== null && Number.isNaN(kommoFieldId)) {
      throw new Error("Mensaje IA Field ID debe ser numérico");
    }

    const monthlyTokenBudget = Number(form.monthlyTokenBudget ?? 0);
    if (Number.isNaN(monthlyTokenBudget) || monthlyTokenBudget < 0) {
      throw new Error("Budget mensual debe ser un número válido");
    }

    const temperature = Number(form.temperature ?? 0);
    if (Number.isNaN(temperature) || temperature < 0 || temperature > 2) {
      throw new Error("Temperature debe estar entre 0 y 2");
    }

    const kommoAccessToken =
      form.kommoAccessToken && form.kommoAccessToken.trim() !== ""
        ? form.kommoAccessToken
        : null;

    const kommoScopeId =
      form.kommoScopeId && form.kommoScopeId.trim() !== ""
        ? form.kommoScopeId
        : null;

    return {
      slug: form.slug,                 // el back puede o no leerlo desde body en update
      displayName: form.displayName,
      isActive: !!form.isActive,
      kommoBaseUrl: form.kommoBaseUrl,

      iaProvider: form.iaProvider || "openai",
      iaModel: form.iaModel || "gpt-4o-mini",
      monthlyTokenBudget,
      temperature,
      systemPrompt: form.systemPrompt || "",

      kommoAccessToken,
      kommoMensajeIaFieldId: kommoFieldId,
      kommoScopeId,

      businessRulesJson
    };
  }

  // =======================
  // GUARDAR (crear/editar)
  // =======================
  async function save() {
    // (3) Validaciones suaves inmediatas
    if (!form.slug?.trim()) {
      toast("error", "El slug es obligatorio");
      return;
    }
    if (!form.displayName?.trim()) {
      toast("error", "El nombre es obligatorio");
      return;
    }
    if (!/^https?:\/\//i.test(form.kommoBaseUrl || "")) {
      toast("error", "La URL de Kommo debe comenzar con http o https");
      return;
    }

    // Normalización y validaciones de detalle
    let payload;
    try {
      payload = buildPayload();
    } catch (e) {
      toast("error", e.message);
      return;
    }

    try {
      setSaving(true);

      if (edit) {
        await api.updateTenant(form.slug, payload);  // PUT /admin/admintenants/{slug}
        toast("success", "Cambios guardados");
      } else {
        await api.createTenant(payload);             // POST /admin/admintenants
        toast("success", "Tenant creado");
      }

      setOpen(false);
      await load(); // refresca la lista
    } catch (err) {
      const nice = parseApiError(err);
      toast("error", `No se pudo guardar: ${nice}`);
    } finally {
      setSaving(false);
    }
  }

  // =======================
  // RENDER
  // =======================
  return (
    <div className="p-4 space-y-4">
      {/* Header: búsqueda + botón crear */}
      <div className="flex items-center justify-between">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Buscar tenants…"
          className="border rounded px-3 py-2 w-80"
        />
        <button
          onClick={openCreate}
          className="px-3 py-2 bg-blue-600 text-white rounded hover:bg-blue-700"
        >
          + Nuevo Tenant
        </button>
      </div>

      {/* Tabla */}
      <div className="border rounded overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50">
            <tr>
              <th className="p-3 text-left">SLUG</th>
              <th className="p-3 text-left">NOMBRE</th>
              <th className="p-3 text-left">MODELO IA</th>
              <th className="p-3 text-left">ESTADO</th>
              <th className="p-3 text-left">ACCIONES</th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr>
                <td colSpan="5" className="p-4">
                  Cargando…
                </td>
              </tr>
            )}
            {!loading && filtered.length === 0 && (
              <tr>
                <td colSpan="5" className="p-4 text-gray-500">
                  Sin resultados
                </td>
              </tr>
            )}
            {!loading &&
              filtered.map((t) => (
                <tr key={t.slug} className="border-t">
                  <td className="p-3">{t.slug}</td>
                  <td className="p-3">{t.displayName}</td>
                  <td className="p-3">{t.iaModel}</td>
                  <td className="p-3">
                    {t.isActive ? (
                      <span className="text-green-700">Activo</span>
                    ) : (
                      <span className="text-gray-500">Inactivo</span>
                    )}
                  </td>
                  <td className="p-3 space-x-3">
                    <button
                      onClick={() => openEdit(t)}
                      className="text-indigo-600 hover:text-indigo-800"
                    >
                      Editar
                    </button>
                  </td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>

      {/* Modal Crear/Editar */}
      <Modal
        title={edit ? "Editar tenant" : "Nuevo tenant"}
        open={open}
        // (4) Evitar cerrar mientras guarda
        onClose={() => !saving && setOpen(false)}
        footer={
          <>
            <button
              disabled={saving}
              onClick={() => setOpen(false)}
              className="px-4 py-2 bg-gray-200 rounded mr-2"
            >
              Cancelar
            </button>
            <button
              onClick={save}
              disabled={saving}
              className={`px-4 py-2 rounded text-white ${
                saving ? "bg-gray-400" : "bg-blue-600 hover:bg-blue-700"
              }`}
            >
              {edit ? "Guardar cambios" : "Crear tenant"}
            </button>
          </>
        }
      >
        {/* Tabs */}
        <div className="flex gap-2 mb-3">
          {[
            ["basic", "Básico"],
            ["ia", "IA"],
            ["kommo", "Kommo"],
            ["br", "Business Rules"],
          ].map(([key, label]) => (
            <button
              key={key}
              onClick={() => setTab(key)}
              className={`px-3 py-1 rounded border ${
                tab === key
                  ? "bg-blue-50 border-blue-300 text-blue-700"
                  : "bg-white"
              }`}
            >
              {label}
            </button>
          ))}
        </div>

        {/* BASIC */}
        {tab === "basic" && (
          <div className="space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-xs text-gray-600">Slug*</label>
                <input
                  ref={slugRef}
                  value={form.slug}
                  onChange={(e) => {
                    // Normaliza slug: minúsculas, números y guiones
                    const v = e.target.value
                      .toLowerCase()
                      .replace(/[^a-z0-9-]/g, "");
                    setForm({ ...form, slug: v });
                  }}
                  disabled={!!edit}
                  className="w-full border rounded px-3 py-2"
                />
              </div>
              <div>
                <label className="text-xs text-gray-600">
                  Nombre (displayName)*
                </label>
                <input
                  value={form.displayName}
                  onChange={(e) =>
                    setForm({ ...form, displayName: e.target.value })
                  }
                  className="w-full border rounded px-3 py-2"
                />
              </div>
            </div>

            <div>
              <label className="text-xs text-gray-600">Kommo Base URL*</label>
              <input
                value={form.kommoBaseUrl}
                onChange={(e) =>
                  setForm({ ...form, kommoBaseUrl: e.target.value })
                }
                className="w-full border rounded px-3 py-2"
                placeholder="https://miempresa.kommo.com"
              />
            </div>

            <label className="inline-flex items-center gap-2">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) =>
                  setForm({ ...form, isActive: e.target.checked })
                }
              />
              <span>Activo</span>
            </label>
          </div>
        )}

        {/* IA */}
        {tab === "ia" && (
          <div className="space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-xs text-gray-600">Modelo IA</label>
                <input
                  value={form.iaModel}
                  onChange={(e) =>
                    setForm({ ...form, iaModel: e.target.value })
                  }
                  className="w-full border rounded px-3 py-2"
                />
              </div>
              <div>
                <label className="text-xs text-gray-600">
                  Temperature (0–2)
                </label>
                <input
                  type="number"
                  step="0.1"
                  min="0"
                  max="2"
                  value={form.temperature}
                  onChange={(e) =>
                    setForm({
                      ...form,
                      temperature:
                        e.target.value === "" ? "" : Number(e.target.value),
                    })
                  }
                  className="w-full border rounded px-3 py-2"
                />
              </div>
            </div>

            <div>
              <label className="text-xs text-gray-600">
                Budget mensual (tokens)
              </label>
              <input
                type="number"
                value={form.monthlyTokenBudget}
                onChange={(e) =>
                  setForm({
                    ...form,
                    monthlyTokenBudget:
                      e.target.value === "" ? "" : Number(e.target.value),
                  })
                }
                className="w-full border rounded px-3 py-2"
              />
            </div>

            <div>
              <label className="text-xs text-gray-600">System Prompt</label>
              <textarea
                rows={6}
                value={form.systemPrompt}
                onChange={(e) =>
                  setForm({ ...form, systemPrompt: e.target.value })
                }
                className="w-full border rounded px-3 py-2"
              />
            </div>
          </div>
        )}

        {/* KOMMO */}
        {tab === "kommo" && (
          <div className="space-y-3">
            <div>
              <label className="text-xs text-gray-600">Access Token</label>
              <input
                value={form.kommoAccessToken}
                onChange={(e) =>
                  setForm({ ...form, kommoAccessToken: e.target.value })
                }
                className="w-full border rounded px-3 py-2"
                placeholder="kmm_xxx…"
              />
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-xs text-gray-600">
                  Mensaje IA Field ID
                </label>
                <input
                  value={form.kommoMensajeIaFieldId}
                  onChange={(e) => {
                    // sólo dígitos
                    const v = e.target.value.replace(/[^\d]/g, "");
                    setForm({ ...form, kommoMensajeIaFieldId: v });
                  }}
                  inputMode="numeric"
                  placeholder="Ej: 123456789"
                  className="w-full border rounded px-3 py-2"
                />
              </div>
              <div>
                <label className="text-xs text-gray-600">Scope Id (opcional)</label>
                <input
                  value={form.kommoScopeId}
                  onChange={(e) =>
                    setForm({ ...form, kommoScopeId: e.target.value })
                  }
                  className="w-full border rounded px-3 py-2"
                  placeholder="chatscope_xxx (si aplica)"
                />
              </div>
            </div>
          </div>
        )}

        {/* BUSINESS RULES */}
        {tab === "br" && (
          <div className="space-y-2">
            <label className="text-xs text-gray-600">Business Rules (JSON)</label>
            <textarea
              rows={8}
              value={form.businessRulesJson}
              onChange={(e) =>
                setForm({ ...form, businessRulesJson: e.target.value })
              }
              className="w-full border rounded px-3 py-2 font-mono text-xs"
              placeholder='{"citas":{"ciudades":["medellín","bogotá"]}}'
            />
          </div>
        )}
      </Modal>
    </div>
  );
}
