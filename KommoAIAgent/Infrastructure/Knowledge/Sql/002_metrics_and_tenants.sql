-- =====================================================================
-- KommoAIAgent — Tenants, Metrics & Logs Schema
-- Archivo: 002_metrics_and_tenants.sql
-- Ejecutar DESPUÉS de 001_kb_schema.sql
-- =====================================================================

-- ---------------------------------------------------------------------
-- TABLA: tenants
-- Catálogo de clientes multi-tenant con su configuración
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tenants (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    
    -- Identidad y ruteo
    "Slug" VARCHAR(100) NOT NULL UNIQUE,
    "DisplayName" VARCHAR(200) NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT true,
    
    -- Kommo
    "KommoBaseUrl" VARCHAR(200) NOT NULL,
    "KommoAccessToken" TEXT,
    "KommoMensajeIaFieldId" BIGINT,
    "KommoScopeId" VARCHAR(100),
    
    -- IA
    "IaProvider" VARCHAR(30) NOT NULL DEFAULT 'OpenAI',
    "IaModel" VARCHAR(120) NOT NULL DEFAULT 'gpt-4o-mini',
    "Temperature" REAL,
    "TopP" REAL,
    "MaxTokens" INTEGER,
    "SystemPrompt" TEXT,
    "BusinessRulesJson" TEXT,
    
    -- Budget & guardrails
    "MonthlyTokenBudget" INTEGER NOT NULL DEFAULT 5000000,
    "AlertThresholdPct" INTEGER NOT NULL DEFAULT 75,
    
    -- Runtime defaults
    "MemoryTTLMinutes" INTEGER NOT NULL DEFAULT 120,
    "ImageCacheTTLMinutes" INTEGER NOT NULL DEFAULT 3,
    "DebounceMs" INTEGER NOT NULL DEFAULT 700,
    "RatePerMinute" INTEGER NOT NULL DEFAULT 15,
    "RatePer5Minutes" INTEGER NOT NULL DEFAULT 60,
    
    -- Auditoría
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_tenants_slug ON tenants("Slug");
CREATE INDEX IF NOT EXISTS ix_tenants_active ON tenants("IsActive") WHERE "IsActive" = true;

COMMENT ON TABLE tenants IS 'Catálogo de clientes multi-tenant';
COMMENT ON COLUMN tenants."KommoMensajeIaFieldId" IS 'ID del campo personalizado en Kommo donde se escribe la respuesta IA';

-- ---------------------------------------------------------------------
-- TABLA: ia_usage_daily
-- Métricas diarias agregadas por tenant/provider/model
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ia_usage_daily (
    tenant_slug TEXT NOT NULL,
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    day DATE NOT NULL,
    
    -- Contadores
    embedding_chars BIGINT NOT NULL DEFAULT 0,
    input_tokens BIGINT NOT NULL DEFAULT 0,
    output_tokens BIGINT NOT NULL DEFAULT 0,
    calls BIGINT NOT NULL DEFAULT 0,
    errors BIGINT NOT NULL DEFAULT 0,
    
    -- Auditoría
    created_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    PRIMARY KEY (tenant_slug, provider, model, day)
);

CREATE INDEX IF NOT EXISTS ix_usage_tenant_day ON ia_usage_daily(tenant_slug, day DESC);
CREATE INDEX IF NOT EXISTS ix_usage_provider_model ON ia_usage_daily(provider, model);

COMMENT ON TABLE ia_usage_daily IS 'Uso diario de IA por tenant (para presupuestos y reportes)';
COMMENT ON COLUMN ia_usage_daily.embedding_chars IS 'Caracteres procesados en embeddings (no tokens)';

-- ---------------------------------------------------------------------
-- TABLA: ia_logs
-- Registro detallado de errores y eventos importantes de IA
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ia_logs (
    id BIGSERIAL PRIMARY KEY,
    when_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    tenant_slug TEXT,
    provider TEXT,
    model TEXT,
    operation TEXT NOT NULL,
    message TEXT NOT NULL,
    raw JSONB,
    
    -- Índices para búsqueda
    CONSTRAINT chk_operation_not_empty CHECK (operation <> '')
);

CREATE INDEX IF NOT EXISTS ix_logs_tenant_when ON ia_logs(tenant_slug, when_utc DESC);
CREATE INDEX IF NOT EXISTS ix_logs_operation ON ia_logs(operation);
CREATE INDEX IF NOT EXISTS ix_logs_when ON ia_logs(when_utc DESC);

COMMENT ON TABLE ia_logs IS 'Logs de errores y eventos críticos de IA';
COMMENT ON COLUMN ia_logs.raw IS 'Payload JSON original del error (para debugging)';

-- ---------------------------------------------------------------------
-- TABLA: ia_costs
-- Catálogo de costos por provider/model (configurable sin redeploy)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ia_costs (
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    
    -- Costos en USD por 1K tokens/chars
    input_per_1k NUMERIC(10,6) NOT NULL DEFAULT 0,
    output_per_1k NUMERIC(10,6) NOT NULL DEFAULT 0,
    emb_per_1k_tokens NUMERIC(10,6) NOT NULL DEFAULT 0,
    
    updated_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    PRIMARY KEY (provider, model)
);

COMMENT ON TABLE ia_costs IS 'Catálogo de costos de IA (actualizable en runtime)';

-- Datos iniciales de costos (OpenAI - Octubre 2025)
INSERT INTO ia_costs (provider, model, input_per_1k, output_per_1k, emb_per_1k_tokens) VALUES
('openai', 'gpt-4o', 0.0025, 0.01, 0),
('openai', 'gpt-4o-mini', 0.00015, 0.0006, 0),
('openai', 'text-embedding-3-small', 0, 0, 0.00002),
('openai', 'text-embedding-3-large', 0, 0, 0.00013)
ON CONFLICT (provider, model) DO NOTHING;

-- ---------------------------------------------------------------------
-- VISTA: Reporte mensual por tenant
-- ---------------------------------------------------------------------
CREATE OR REPLACE VIEW v_monthly_usage AS
SELECT 
    tenant_slug,
    provider,
    model,
    DATE_TRUNC('month', day) AS month,
    SUM(embedding_chars) AS total_emb_chars,
    SUM(input_tokens) AS total_input_tokens,
    SUM(output_tokens) AS total_output_tokens,
    SUM(calls) AS total_calls,
    SUM(errors) AS total_errors,
    MAX(updated_utc) AS last_updated
FROM ia_usage_daily
GROUP BY tenant_slug, provider, model, DATE_TRUNC('month', day)
ORDER BY month DESC, total_calls DESC;

COMMENT ON VIEW v_monthly_usage IS 'Resumen mensual de uso de IA por tenant';

-- ---------------------------------------------------------------------
-- FUNCIÓN: Trigger para updated_utc en tenants
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION update_tenant_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW."UpdatedAt" = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_tenants_update
    BEFORE UPDATE ON tenants
    FOR EACH ROW
    EXECUTE FUNCTION update_tenant_timestamp();

-- ---------------------------------------------------------------------
-- DATOS DE EJEMPLO (opcional - comentar en producción)
-- ---------------------------------------------------------------------
/*
INSERT INTO tenants (
    "Slug", "DisplayName", "KommoBaseUrl", "KommoMensajeIaFieldId",
    "IaModel", "MonthlyTokenBudget"
) VALUES (
    'demo',
    'Demo Company',
    'https://demo.kommo.com',
    123456,
    'gpt-4o-mini',
    2000000
) ON CONFLICT ("Slug") DO NOTHING;
*/

-- ---------------------------------------------------------------------
-- Verificación de instalación
-- ---------------------------------------------------------------------
DO $$
BEGIN
    RAISE NOTICE 'Schema 002 instalado correctamente';
    RAISE NOTICE 'Tablas creadas: tenants, ia_usage_daily, ia_logs, ia_costs';
    RAISE NOTICE 'Vista creada: v_monthly_usage';
END $$;