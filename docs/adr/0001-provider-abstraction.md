# ADR 0001: Keep provider schemas behind an internal port

**Status:** Accepted

## Context

Applications that depend directly on one vendor's request/response objects become hard to fail over, meter consistently or govern centrally.

## Decision

The public boundary uses a narrow OpenAI-compatible chat contract while `ILlmProvider` owns the internal provider port. Providers return normalized token counts, content and latency.

## Consequences

- Routing policy can switch providers without changing clients.
- Provider-specific features need explicit capability modeling rather than leaking through.
- The fake providers can exercise resilience paths without credentials.
