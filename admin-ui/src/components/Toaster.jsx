import React, { useEffect, useState } from "react";

/**
 * Dispara un toast global.
 *  kind: 'success' | 'error' | 'info' | 'warn'
 *  message: string
 */
export function toast(kind, message, duration = 3500) {
  if (typeof window === "undefined") return;
  window.dispatchEvent(
    new CustomEvent("app:toast", { detail: { kind, message, duration } })
  );
}

/**
 * Toaster global. Debe montarse una sola vez (por ejemplo en App.jsx).
 * Escucha 'app:toast' en window y muestra una cola de notificaciones.
 */
export default function Toaster() {
  const [queue, setQueue] = useState([]);

  useEffect(() => {
    if (typeof window === "undefined") return;

    const onToast = (e) => {
      const { kind = "info", message = "", duration = 3500 } = e.detail || {};
      const id = Math.random().toString(36).slice(2);
      setQueue((q) => [...q, { id, kind, message, duration }]);

      // autodestruir después de duration
      window.setTimeout(() => {
        setQueue((q) => q.filter((t) => t.id !== id));
      }, Math.max(1500, duration));
    };

    window.addEventListener("app:toast", onToast);
    return () => window.removeEventListener("app:toast", onToast);
  }, []);

  if (!queue.length) return null;

  return (
    <div className="fixed top-4 right-4 z-50 space-y-2">
      {queue.map((t) => (
        <ToastItem key={t.id} kind={t.kind} message={t.message} />
      ))}
    </div>
  );
}

function ToastItem({ kind, message }) {
  const styles =
    kind === "error"
      ? "bg-red-600"
      : kind === "success"
      ? "bg-green-600"
      : kind === "warn"
      ? "bg-yellow-600"
      : "bg-slate-700";

  return (
    <div className={`${styles} text-white px-4 py-3 rounded shadow-lg min-w-[260px]`}>
      <div className="font-semibold capitalize">{kind}</div>
      <div className="text-sm opacity-90">{message}</div>
    </div>
  );
}
