# Threat model

## Assets

- Tenant API keys
- Budget integrity
- Provider credentials in future adapters
- Prompt confidentiality
- Usage telemetry
- Availability of the routing layer

## Primary threats and controls

| Threat | Control |
|---|---|
| Stolen client key | Only digests are stored; per-tenant rate and budget caps reduce blast radius |
| Prompt data leakage | Redaction before provider boundary; prompt bodies are not persisted |
| Cost amplification | Atomic budget reservation before routing and max-token clamp |
| Noisy tenant | Redis tenant/minute limiter |
| Provider outage | Timeout, bounded retry, circuit opening and provider fallback |
| Retry storm | Circuit state prevents continually hitting a failing provider |
| Secret committed to repo | No real provider credential is required; `.env*` ignored except example |
| Container escape surface | Non-root runtime, read-only filesystem and `no-new-privileges` in Compose |

## Residual risk

Regex redaction can miss PII and can false-positive. A production deployment should combine data classification, allow/deny policies, provider retention settings, encryption/key management and organization-specific audit requirements.
