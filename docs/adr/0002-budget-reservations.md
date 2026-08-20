# ADR 0002: Reserve worst-case cost before provider execution

**Status:** Accepted

## Context

Checking a monthly spend total and then calling a model is race-prone. Concurrent requests can all observe remaining budget and overshoot it.

## Decision

Before routing, ModelRelay estimates the maximum request cost and creates a reservation under a serializable transaction. Active reservations count against remaining budget. On success the reservation is deleted while actual spend and usage are committed together. Failed requests release the reservation; abandoned reservations stop counting after fifteen minutes.

## Consequences

- Concurrent requests cannot all spend the same remaining budget.
- A crash can temporarily reduce available budget, but does not strand it forever.
- The estimate must upper-bound provider output for strong enforcement.
