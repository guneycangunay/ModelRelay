# Contributing

ModelRelay is intentionally small enough to understand in one sitting and strict enough to demonstrate production engineering trade-offs.

## Local checks

```bash
dotnet restore ModelRelay.slnx
dotnet build ModelRelay.slnx --configuration Release --no-restore
dotnet test ModelRelay.slnx --configuration Release --no-build
npm --prefix ui install --no-audit --no-fund
npm --prefix ui run build
```

For the full stack, copy `.env.example` to `.env` and run `docker compose up --build`.

## Pull requests

Keep changes focused. Add or update tests for routing, redaction, budget accounting, and authentication behavior. Do not commit real provider credentials, production tenant data, prompts containing personal data, or generated build output.

Changes to routing or financial/budget invariants should include a short design note in the pull request. Architectural changes that alter trust boundaries or durable state should add an ADR under `docs/adr/`.
