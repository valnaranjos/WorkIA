-- =====================================================================
-- KommoAIAgent — KB Schema (pgvector)
-- Archivo: 001_kb_schema.sql
-- Objetivo: Tablas, restricciones e índices para RAG con Postgres+pgvector
-- Ejecutar: una sola vez por base de datos (por entorno)
-- =====================================================================

-- ⚠️ Requisito previo: la extensión pgvector debe estar instalada en el servidor.
-- Para habilitarla en ESTA base (una vez por DB):
CREATE EXTENSION IF NOT EXISTS vector;

-- ---------------------------------------------------------------------
-- TABLA: kb_documents
-- Un documento por tenant (p.ej., una página, FAQ o artículo), que se
-- trocea (chunking) para generar embeddings en kb_chunks.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS kb_documents (
  id              BIGSERIAL PRIMARY KEY,
  tenant_slug     TEXT NOT NULL,                -- aislamiento lógico multi-tenant
  source_id       TEXT NOT NULL,                -- id lógico único por tenant (p.ej. "faq-envios-001")
  title           TEXT,
  content         TEXT NOT NULL,                -- texto original (sin chunking)
  content_tokens  INT  NOT NULL DEFAULT 0,      -- opcional: conteo de tokens del documento
  tags            TEXT[] DEFAULT '{}',          -- etiquetas/filtros
  created_utc     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_utc     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Unicidad por tenant + source_id (evita duplicados del mismo documento)
CREATE UNIQUE INDEX IF NOT EXISTS ux_kb_documents_tenant_source
  ON kb_documents(tenant_slug, source_id);

-- Búsquedas por tenant (útil para reportes/mantenimiento)
CREATE INDEX IF NOT EXISTS ix_kb_documents_tenant
  ON kb_documents(tenant_slug);

-- ---------------------------------------------------------------------
-- TABLA: kb_chunks
-- Trozos del documento con su embedding.
-- IMPORTANTE: la dimensión del VECTOR debe corresponder al modelo usado.
--  - text-embedding-3-small => 1536
--  - text-embedding-3-large => 3072
-- Si cambias de modelo, ajusta la dimensión y re-embebe.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS kb_chunks (
  id            BIGSERIAL PRIMARY KEY,
  tenant_slug   TEXT NOT NULL,
  document_id   BIGINT NOT NULL REFERENCES kb_documents(id) ON DELETE CASCADE,
  chunk_index   INT    NOT NULL,                -- posición del trozo dentro del documento
  text          TEXT   NOT NULL,                -- contenido del trozo (lo que se indexa)
  embedding     VECTOR(1536),                   -- ⚠️ ajusta si cambias de modelo
  token_count   INT    NOT NULL DEFAULT 0,
  created_utc   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Índices auxiliares
CREATE INDEX IF NOT EXISTS ix_kb_chunks_tenant
  ON kb_chunks(tenant_slug);

CREATE INDEX IF NOT EXISTS ix_kb_chunks_doc
  ON kb_chunks(document_id);

-- ---------------------------------------------------------------------
-- ÍNDICE VECTORIAL para búsqueda por similitud
-- Recomendación: HNSW (mejor recall/latencia). Si no está soportado,
-- caer en IVF Flat como fallback.
-- ---------------------------------------------------------------------
DO $$
BEGIN
  BEGIN
    -- Opción A (preferida): HNSW (pgvector >= 0.5)
    CREATE INDEX ix_kb_chunks_embedding_hnsw
      ON kb_chunks USING hnsw (embedding vector_cosine_ops)
      WITH (m = 16, ef_construction = 200);
  EXCEPTION WHEN undefined_object THEN
    -- Opción B (fallback): IVF Flat
    CREATE INDEX IF NOT EXISTS ix_kb_chunks_embedding
      ON kb_chunks USING ivfflat (embedding vector_cosine_ops)
      WITH (lists = 100);

    -- Recomendado por pgvector tras crear ivfflat
    ANALYZE kb_chunks;
  END;
END$$;

-- ---------------------------------------------------------------------
-- Notas de operación
-- ---------------------------------------------------------------------
-- 1) Si usas HNSW, puedes ajustar el recall en tiempo de consulta:
--    SET hnsw.ef_search = 80;  -- (40–120, sube para más precisión)
--
-- 2) Si usas IVF Flat:
--    SET ivfflat.probes = 10;  -- % de listas exploradas (~10–20)
--
-- 3) Si cambias de modelo (dimensión distinta):
--    - ALTER TABLE kb_chunks ALTER COLUMN embedding TYPE VECTOR(<nueva_dim>);
--    - Recalcular embeddings de todos los chunks.
--
-- 4) Limpieza por tenant/documento:
--    - DELETE FROM kb_documents WHERE tenant_slug = 'foo' AND source_id = 'bar';
--      (elimina en cascada chunks del documento)
--
-- 5) Métricas útiles:
--    SELECT tenant_slug, COUNT(*) FROM kb_documents GROUP BY tenant_slug;
--    SELECT tenant_slug, COUNT(*) FROM kb_chunks GROUP BY tenant_slug;
--
-- 6) Ver plan de ejecución (comprobación de índice):
--    EXPLAIN (ANALYZE, BUFFERS)
--    SELECT id FROM kb_chunks
--    ORDER BY embedding <=> ('[0.1,0.2,0.3]'::vector) LIMIT 5;
--
-- Fin del schema KB
-- =====================================================================
