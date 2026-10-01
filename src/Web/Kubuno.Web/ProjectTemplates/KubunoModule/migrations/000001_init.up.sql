-- $moduletitle$ - initial schema. Every table of the module lives in its own schema ("$moduleid$"), never in
-- public nor in another module's schema.
CREATE SCHEMA IF NOT EXISTS "$moduleid$";

CREATE TABLE IF NOT EXISTS "$moduleid$".items (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id   UUID NOT NULL,
    name       TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_items_owner ON "$moduleid$".items (owner_id);
