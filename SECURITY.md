# Security policy

ModelRelay is an architecture reference, not a hosted AI proxy.

Please report security issues privately rather than opening a public issue. Never include real provider credentials, API keys, personal data, or production prompts in a report.

## Security posture

- Client API keys are stored only as SHA-256 digests.
- Prompt bodies are not persisted by the usage store.
- Basic email, secret-like token and long-number redaction runs before provider routing.
- Monthly budget is reserved before provider execution.
- Redis-backed rate limits apply per tenant.
- The runtime container is non-root and compatible with a read-only filesystem.
- The included providers are deterministic simulators and require no vendor credentials.

The sample redactor is deliberately narrow. Production deployments should use organization-specific DLP/classification policies and should not assume regular expressions provide complete PII protection.
