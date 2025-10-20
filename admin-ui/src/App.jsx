import React, { useState } from "react";
import PropTypes from "prop-types";
import TenantsView from "./views/TenantsView";
import KnowledgeView from "./views/KnowledgeView";
import MetricsView from "./views/MetricsView";
import Toaster from "./components/Toaster";
import DashboardView from "./views/DashboardView";
import LogsView from "./views/LogsView";
import CostsView from "./views/CostsView";

export default function App() {
  const [section, setSection] = useState("dashboard"); // 'dashboard' | 'tenants' | 'kb' | 'metrics' | 'logs'

  return (
    <div className="min-h-screen flex">
      {/* SIDEBAR */}
      <aside className="w-64 border-r p-4 space-y-2">
        <div className="font-semibold text-lg mb-4">KommoAI Admin</div>
        <NavItem label="Dashboard" active={section==='dashboard'} onClick={()=>setSection('dashboard')} />
        <NavItem label="Tenants" active={section==='tenants'} onClick={()=>setSection('tenants')} />
        <NavItem label="Knowledge Base" active={section==='kb'} onClick={()=>setSection('kb')} />
        <NavItem label="Métricas" active={section==='metrics'} onClick={()=>setSection('metrics')} />
          <NavItem label="Costos IA" active={section==='costs'} onClick={()=>setSection('costs')} />
       <NavItem label="Logs" active={section==='logs'} onClick={()=>setSection('logs')} />
      </aside>

      {/* CONTENT */}
      <main className="flex-1">
        {section === 'tenants' && <TenantsView />}
        {section === 'kb' && <KnowledgeView />}
        {section === 'metrics' && <MetricsView />}
         {section === 'dashboard' && <DashboardView />} 
         {section==='costs' && <CostsView />}
         {section === 'logs'      && <LogsView />}
      </main>

      {/* TOASTER GLOBAL */}
      <Toaster />
    </div>
  );
}
function NavItem({ label, active, onClick }) {
  return (
    <button onClick={onClick}
      className={`w-full text-left px-3 py-2 rounded ${active ? 'bg-blue-50 text-blue-700' : 'hover:bg-gray-100'}`}>
      {label}
    </button>
  );
}

NavItem.propTypes = {
  label: PropTypes.string.isRequired,
  active: PropTypes.bool,
  onClick: PropTypes.func,
};

