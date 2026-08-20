CREATE TABLE IF NOT EXISTS tenants (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    api_key_hash text NOT NULL UNIQUE,
    monthly_budget_microusd bigint NOT NULL CHECK (monthly_budget_microusd >= 0),
    requests_per_minute integer NOT NULL CHECK (requests_per_minute > 0),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS tenant_budget_month (
    tenant_id uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    month_key char(7) NOT NULL,
    spent_microusd bigint NOT NULL DEFAULT 0 CHECK (spent_microusd >= 0),
    PRIMARY KEY (tenant_id, month_key)
);

CREATE TABLE IF NOT EXISTS budget_reservations (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    month_key char(7) NOT NULL,
    amount_microusd bigint NOT NULL CHECK (amount_microusd >= 0),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_budget_reservations_tenant_month_created
    ON budget_reservations (tenant_id, month_key, created_at);

CREATE TABLE IF NOT EXISTS usage_requests (
    request_id text PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    model text NOT NULL,
    provider text NOT NULL,
    prompt_tokens integer NOT NULL CHECK (prompt_tokens >= 0),
    completion_tokens integer NOT NULL CHECK (completion_tokens >= 0),
    cost_microusd bigint NOT NULL CHECK (cost_microusd >= 0),
    latency_ms bigint NOT NULL CHECK (latency_ms >= 0),
    fallback_count integer NOT NULL DEFAULT 0 CHECK (fallback_count >= 0),
    redaction_count integer NOT NULL DEFAULT 0 CHECK (redaction_count >= 0),
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_usage_requests_tenant_created
    ON usage_requests (tenant_id, created_at DESC);
