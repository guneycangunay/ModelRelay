import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'

type ProviderSummary = {
  provider: string
  requests: number
  avgLatencyMs: number
}

type Summary = {
  requests24h: number
  tokens24h: number
  costMonthMicroUsd: number
  budgetMicroUsd: number
  p95LatencyMs: number
  fallbacks24h: number
  providers: ProviderSummary[]
}

const apiBase = import.meta.env.VITE_API_BASE ?? ''

function formatUsd(microUsd: number) {
  return `$${(microUsd / 1_000_000).toFixed(4)}`
}

function App() {
  const [apiKey, setApiKey] = useState('relay_demo_key_change_me')
  const [summary, setSummary] = useState<Summary | null>(null)
  const [error, setError] = useState('')
  const [prompt, setPrompt] = useState('Explain why idempotency matters in payment APIs.')
  const [model, setModel] = useState('relay-fast')
  const [answer, setAnswer] = useState('')
  const [routing, setRouting] = useState('')
  const [loading, setLoading] = useState(false)

  const loadSummary = useCallback(async () => {
    setError('')
    try {
      const response = await fetch(`${apiBase}/admin/metrics/summary`, {
        headers: { 'X-Api-Key': apiKey }
      })
      if (!response.ok) throw new Error(`Dashboard request failed (${response.status})`)
      setSummary(await response.json())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to load dashboard')
    }
  }, [apiKey])

  useEffect(() => {
    void loadSummary()
  }, [loadSummary])

  const budgetPercent = useMemo(() => {
    if (!summary || summary.budgetMicroUsd === 0) return 0
    return Math.min(100, (summary.costMonthMicroUsd / summary.budgetMicroUsd) * 100)
  }, [summary])

  async function submitPrompt(event: FormEvent) {
    event.preventDefault()
    setLoading(true)
    setAnswer('')
    setRouting('')
    setError('')

    try {
      const response = await fetch(`${apiBase}/v1/chat/completions`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Api-Key': apiKey
        },
        body: JSON.stringify({
          model,
          messages: [{ role: 'user', content: prompt }],
          max_tokens: 180
        })
      })

      if (!response.ok) {
        const problem = await response.json().catch(() => null)
        throw new Error(problem?.title ?? `Relay request failed (${response.status})`)
      }

      const data = await response.json()
      setAnswer(data.choices?.[0]?.message?.content ?? 'No response')
      const provider = response.headers.get('X-ModelRelay-Provider') ?? 'unknown'
      const fallbacks = response.headers.get('X-ModelRelay-Fallbacks') ?? '0'
      const redactions = response.headers.get('X-ModelRelay-Redactions') ?? '0'
      setRouting(`${provider} · ${fallbacks} fallback(s) · ${redactions} redaction(s)`)
      await loadSummary()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Request failed')
    } finally {
      setLoading(false)
    }
  }

  return (
    <main>
      <header className="topbar">
        <div>
          <div className="eyebrow">AI RELIABILITY CONTROL PLANE</div>
          <h1>ModelRelay</h1>
          <p>Route, govern and observe model traffic without coupling clients to a provider.</p>
        </div>
        <div className="keybox">
          <label htmlFor="apiKey">Sandbox API key</label>
          <input id="apiKey" value={apiKey} onChange={e => setApiKey(e.target.value)} />
        </div>
      </header>

      {error && <div className="alert">{error}</div>}

      <section className="cards">
        <Metric label="Requests · 24h" value={summary?.requests24h ?? 0} />
        <Metric label="Tokens · 24h" value={summary?.tokens24h ?? 0} />
        <Metric label="p95 latency" value={`${Math.round(summary?.p95LatencyMs ?? 0)} ms`} />
        <Metric label="Fallbacks · 24h" value={summary?.fallbacks24h ?? 0} />
      </section>

      <section className="grid">
        <article className="panel">
          <div className="panelHead">
            <div>
              <span className="kicker">GOVERNANCE</span>
              <h2>Monthly budget</h2>
            </div>
            <strong>{formatUsd(summary?.costMonthMicroUsd ?? 0)}</strong>
          </div>
          <div className="budgetTrack">
            <span style={{ width: `${budgetPercent}%` }} />
          </div>
          <div className="budgetMeta">
            <span>{budgetPercent.toFixed(1)}% consumed</span>
            <span>{formatUsd(summary?.budgetMicroUsd ?? 0)} cap</span>
          </div>
          <p className="note">
            Requests reserve worst-case token cost atomically before routing. Stale reservations expire from budget calculations.
          </p>
        </article>

        <article className="panel">
          <div className="panelHead">
            <div>
              <span className="kicker">ROUTING</span>
              <h2>Provider health</h2>
            </div>
            <button onClick={() => void loadSummary()}>Refresh</button>
          </div>
          <div className="providers">
            {(summary?.providers.length ? summary.providers : [
              { provider: 'fake-primary', requests: 0, avgLatencyMs: 0 },
              { provider: 'fake-secondary', requests: 0, avgLatencyMs: 0 }
            ]).map(provider => (
              <div className="provider" key={provider.provider}>
                <div>
                  <strong>{provider.provider}</strong>
                  <span>{provider.requests} requests</span>
                </div>
                <div className="providerStats">
                  <span>{Math.round(provider.avgLatencyMs)} ms</span>
                  <small>average latency</small>
                </div>
              </div>
            ))}
          </div>
        </article>
      </section>

      <section className="grid lower">
        <article className="panel playground">
          <span className="kicker">OPENAI-COMPATIBLE API</span>
          <h2>Try the relay</h2>
          <form onSubmit={submitPrompt}>
            <div className="row">
              <select value={model} onChange={e => setModel(e.target.value)}>
                <option value="relay-fast">relay-fast</option>
                <option value="relay-balanced">relay-balanced</option>
              </select>
              <span className="hint">Add <code>[fail-primary]</code> to test fallback.</span>
            </div>
            <textarea value={prompt} onChange={e => setPrompt(e.target.value)} />
            <button className="primary" disabled={loading}>{loading ? 'Routing…' : 'Send request'}</button>
          </form>
        </article>

        <article className="panel result">
          <span className="kicker">NORMALIZED RESPONSE</span>
          <h2>Last completion</h2>
          <div className="routing">{routing || 'No request yet'}</div>
          <pre>{answer || 'The deterministic sandbox response will appear here.'}</pre>
        </article>
      </section>

      <footer>
        <span>ModelRelay · production-minded AI gateway reference</span>
        <span>No real model credentials required</span>
      </footer>
    </main>
  )
}

function Metric({ label, value }: { label: string, value: string | number }) {
  return (
    <article className="metric">
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  )
}

export default App
