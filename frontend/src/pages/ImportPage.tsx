import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ImportResult } from '../lib/api'
import { useApi } from '../lib/useApi'
import { pickFolder, zipFolder } from '../lib/folder'
import { fmtDate } from '../lib/format'
import { ErrorBox, Notice, Section, Spinner } from '../components/ui'

export default function ImportPage() {
  const history = useApi<{ id: number; importedAt: string; problemCount: number; added: number; updated: number; unchanged: number }[]>('/api/imports')
  const [status, setStatus] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [err, setErr] = useState<string | null>(null)

  async function run() {
    setErr(null); setResult(null)
    try {
      const folder = await pickFolder()
      if (!folder) return
      setStatus(`Reading "${folder.name}"…`)
      const { blob, count } = await zipFolder(folder, setStatus)
      setStatus(`Uploading ${count} files (${(blob.size / 1024).toFixed(0)} KB)…`)
      const res = await api.upload<ImportResult>('/api/import', blob, p => setStatus(p < 100 ? `Uploading… ${p}%` : 'Analysing…'))
      setResult(res)
      history.reload()
    } catch (e: any) { setErr(e.message) } finally { setStatus(null) }
  }

  return (
    <div className="max-w-4xl space-y-4">
      <h1 className="text-xl font-semibold">Import DSA project</h1>
      <Section title="Upload">
        <p className="mb-3 text-sm text-ink2">
          Select the root of your DSA project (the folder with <code className="font-mono">DSA.slnx</code>, <code className="font-mono">src</code> and <code className="font-mono">tools</code>).
          Your browser zips it (skipping <code className="font-mono">bin</code>, <code className="font-mono">obj</code>, <code className="font-mono">.git</code>) and the server stores every problem, its tests and a clean stub in the database.
          Importing again is safe: problems are matched by number and never duplicated, and your attempts are kept.
        </p>
        <button className="btn btn-primary" disabled={!!status} onClick={run}>{status ? <Spinner /> : '⇪'} Choose folder & import</button>
        {status && <span className="ml-3 text-sm text-mute">{status}</span>}
        <div className="mt-3"><ErrorBox error={err} /></div>
      </Section>

      {result && (
        <Section title="Analysis">
          <Notice tone="good">
            {result.problemCount} problems in {result.topics.length} topics · {result.added} added, {result.updated} updated, {result.unchanged} unchanged · {result.sharedFiles} shared build files.
            {' '}<Link className="underline" to="/queue">Go to the queue →</Link>
          </Notice>
          <table className="grid mt-3">
            <thead><tr><th>Topic</th><th>Easy</th><th>Medium</th><th>Hard</th><th>Total</th></tr></thead>
            <tbody>{result.topics.map(t => (
              <tr key={t.topicNo}><td>{t.topicNo} · {t.topic}</td><td>{t.easy}</td><td>{t.medium}</td><td>{t.hard}</td><td>{t.total}</td></tr>
            ))}</tbody>
          </table>
          {result.warnings.length > 0 && (
            <div className="mt-3 space-y-1">{result.warnings.slice(0, 20).map((w, i) => <Notice key={i} tone="warn">{w}</Notice>)}</div>
          )}
        </Section>
      )}

      <Section title="Previous imports">
        {history.data?.length ? (
          <table className="grid"><thead><tr><th>When</th><th>Problems</th><th>Added</th><th>Updated</th><th>Unchanged</th></tr></thead>
            <tbody>{history.data.map(h => (
              <tr key={h.id}><td>{fmtDate(h.importedAt)}</td><td>{h.problemCount}</td><td>{h.added}</td><td>{h.updated}</td><td>{h.unchanged}</td></tr>
            ))}</tbody></table>
        ) : <p className="text-sm text-mute">None yet.</p>}
      </Section>
    </div>
  )
}
