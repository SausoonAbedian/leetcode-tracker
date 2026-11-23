import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AiAnswer, api, Attempt, ProblemDetail, TestCase } from '../lib/api'
import { useApi } from '../lib/useApi'
import { fmtDate, fmtDue, fmtDuration } from '../lib/format'
import { AttemptPanel } from '../components/AttemptPanel'
import { AttemptsChart } from '../components/Charts'
import { CodeDiff, CodeView } from '../components/CodeView'
import { Badge, DifficultyBadge, ErrorBox, Flags, Markdown, Modal, Section, Spinner, StageBadge, StatusBadge } from '../components/ui'

interface AttemptFull { attempt: Attempt; code?: string | null; results?: { tests: TestCase[]; compileErrors: string[]; buildFailed: boolean } | null }

export default function ProblemPage() {
  const { id } = useParams()
  const { data: p, error, loading, reload } = useApi<ProblemDetail>(`/api/problems/${id}`)
  const [open, setOpen] = useState<AttemptFull | null>(null)
  const [diff, setDiff] = useState<{ a: AttemptFull; b: AttemptFull } | null>(null)
  const [diffPick, setDiffPick] = useState<number | null>(null)
  const [edit, setEdit] = useState<Attempt | null>(null)
  const [review, setReview] = useState<AiAnswer | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [showReadme, setShowReadme] = useState(false)

  if (loading && !p) return <Spinner />
  if (!p) return <ErrorBox error={error ?? 'Not found'} />

  const finished = p.attempts.filter(a => a.status === 'Passed' || a.status === 'Failed')
  const loadAttempt = (aid: number) => api.get<AttemptFull>(`/api/attempts/${aid}`)

  async function run<T>(label: string, fn: () => Promise<T>) {
    setBusy(label); setErr(null)
    try { return await fn() } catch (e: any) { setErr(e.message) } finally { setBusy(null) }
  }

  const reviewAttempt = (a: Attempt) => run(`review-${a.id}`, async () => {
    setReview(await api.post<AiAnswer>('/api/ai/review', { problemId: p.id, attemptId: a.id }))
    reload()
  })

  const pickDiff = (a: Attempt) => run(`diff-${a.id}`, async () => {
    if (diffPick == null) { setDiffPick(a.id); return }
    if (diffPick === a.id) { setDiffPick(null); return }
    const [x, y] = await Promise.all([loadAttempt(diffPick), loadAttempt(a.id)])
    const [older, newer] = new Date(x.attempt.createdAt) < new Date(y.attempt.createdAt) ? [x, y] : [y, x]
    setDiff({ a: older, b: newer }); setDiffPick(null)
  })

  const total = finished.length
  const passes = finished.filter(a => a.status === 'Passed').length
  const best = finished.filter(a => a.status === 'Passed').reduce<number | null>((m, a) => m == null || a.activeSeconds < m ? a.activeSeconds : m, null)

  return (
    <div className="space-y-4">
      <div>
        <Link to="/problems" className="text-xs text-mute hover:text-ink">← Problems</Link>
        <div className="mt-1 flex flex-wrap items-center gap-3">
          <h1 className="text-xl font-semibold">
            <span className="font-mono text-mute">NC{String(p.id).padStart(3, '0')}</span> {p.title}
          </h1>
          <DifficultyBadge d={p.difficulty} />
          <Link to={`/queue?topic=${p.topicNo}`} className="text-sm text-ink2 hover:text-accent">{p.topic}</Link>
          <span className="text-sm text-mute">LeetCode #{p.lcNumber}</span>
          <div className="ml-auto flex gap-2">
            <a className="btn btn-sm" href={p.solveUrl} target="_blank" rel="noreferrer">Open statement ↗</a>
            <a className="btn btn-sm" href={p.ncUrl} target="_blank" rel="noreferrer">NeetCode ↗</a>
            <button className="btn btn-sm" onClick={() => setShowReadme(true)}>README</button>
          </div>
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-5 text-sm text-mute">
          <span>{passes}/{total} attempts passed</span>
          <span>Best {fmtDuration(best)}</span>
          <span>Limit {p.limitMinutes} min</span>
          {p.reviewStage > 0 && p.nextReviewAt ? (
            <span className="flex items-center gap-2 text-ink2">
              <StageBadge stage={p.reviewStage} />
              Review stage {p.reviewStage} · next review {fmtDate(p.nextReviewAt)} ({fmtDue(p.nextReviewAt)})
            </span>
          ) : total > 0 ? (
            <span title={`Clean passes move a problem through review stages: ${p.intervals.join(', ')} days (last repeats)`}>
              Not in review — a hint, fail or going over time resets the stages
            </span>
          ) : null}
        </div>
      </div>

      <AttemptPanel problem={p} onChanged={reload} />
      <ErrorBox error={err} onClose={() => setErr(null)} />

      <Section title="Attempts over time">
        <AttemptsChart attempts={p.attempts} limitMinutes={p.limitMinutes} />
      </Section>

      <Section title={`History (${finished.length})`} right={diffPick != null && <span className="text-xs text-accent">Pick another attempt to compare with #{diffPick}…</span>}>
        {finished.length === 0 ? <p className="text-sm text-mute">No finished attempts yet.</p> : (
          <ol className="relative space-y-3 border-l border-line pl-5">
            {finished.map((a, i) => (
              <li key={a.id} className="relative">
                <span className="absolute -left-[26px] top-1.5 size-2.5 rounded-full ring-4 ring-[var(--surface-1)]"
                  style={{ background: a.status === 'Passed' ? 'var(--good)' : 'var(--critical)' }} />
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-medium">Attempt {finished.length - i}</span>
                  <StatusBadge status={a.status} failReason={a.failReason} />
                  <Flags hinted={a.hinted} hints={a.hintsUsed} overTime={a.overTime} />
                  {a.testsTotal != null && <Badge>{a.testsPassed}/{a.testsTotal} tests</Badge>}
                  <span className="font-mono text-sm tabular-nums text-ink2">{fmtDuration(a.activeSeconds)}</span>
                  <span className="text-xs text-mute">{fmtDate(a.startedAt)} → {fmtDate(a.endedAt)}</span>
                  <div className="ml-auto flex flex-wrap gap-1.5">
                    <button className="btn btn-sm" disabled={!a.hasCode} onClick={() => run(`code-${a.id}`, async () => setOpen(await loadAttempt(a.id)))}>
                      {busy === `code-${a.id}` ? <Spinner /> : '</>'} Code
                    </button>
                    <button className="btn btn-sm" disabled={!a.hasCode} title="Download a zip with this code to continue from it"
                      onClick={() => run('restore', () => api.download(`/api/attempts/${a.id}/restore`, 'GET'))}>↺ Restore</button>
                    <button className="btn btn-sm" disabled={!a.hasCode || !!busy} onClick={() => reviewAttempt(a)}>
                      {busy === `review-${a.id}` ? <Spinner /> : '🔍'} Review
                    </button>
                    <button className={`btn btn-sm ${diffPick === a.id ? 'btn-primary' : ''}`} disabled={!a.hasCode} onClick={() => pickDiff(a)}>⇄ Diff</button>
                    <button className="btn btn-sm" onClick={() => setEdit(a)}>Edit</button>
                  </div>
                </div>
                {a.notes && <p className="mt-1 text-sm text-ink2">{a.notes}</p>}
              </li>
            ))}
          </ol>
        )}
      </Section>

      {review && (
        <Section title={`Review · ${review.provider} ${review.model}`} right={<button className="text-mute hover:text-ink" onClick={() => setReview(null)}>✕</button>}>
          <Markdown text={review.text} />
        </Section>
      )}

      <Section title={`AI hints & reviews (${p.ai.length})`}>
        {p.ai.length === 0 ? <p className="text-sm text-mute">No hints or reviews yet.</p> : (
          <div className="space-y-2">
            {p.ai.map(x => (
              <details key={x.id} className="rounded-lg border border-line bg-s0 px-3 py-2">
                <summary className="cursor-pointer text-sm">
                  <Badge tone="violet">{x.kind === 'Review' ? '🔍 Review' : `💡 ${x.kind.replace('Hint', 'Hint ')}`}</Badge>
                  <span className="ml-2 text-xs text-mute">{fmtDate(x.at)} · {x.provider} {x.model}</span>
                </summary>
                <div className="mt-2"><Markdown text={x.response} /></div>
              </details>
            ))}
          </div>
        )}
      </Section>

      <Modal open={!!open} onClose={() => setOpen(null)} title={open ? `Attempt code · ${open.attempt.status} · ${fmtDuration(open.attempt.activeSeconds)}` : ''} wide>
        {open?.code && <CodeView code={open.code} height={560} />}
        {open?.results?.tests && open.results.tests.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-1.5">
            {open.results.tests.map(t => <Badge key={t.name} tone={t.outcome === 'Passed' ? 'good' : 'bad'}>{t.outcome === 'Passed' ? '✓' : '✕'} {t.name}</Badge>)}
          </div>
        )}
        {open?.results?.compileErrors && open.results.compileErrors.length > 0 && (
          <pre className="mt-3 whitespace-pre-wrap font-mono text-xs text-[#f3b0b0]">{open.results.compileErrors.join('\n')}</pre>
        )}
      </Modal>

      <Modal open={!!diff} onClose={() => setDiff(null)} title={diff ? `Diff: attempt from ${fmtDate(diff.a.attempt.createdAt)} → ${fmtDate(diff.b.attempt.createdAt)}` : ''} wide>
        {diff && <CodeDiff left={diff.a.code ?? ''} right={diff.b.code ?? ''} />}
      </Modal>

      {edit && <EditAttempt attempt={edit} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); reload() }} />}

      <Modal open={showReadme} onClose={() => setShowReadme(false)} title="README" wide>
        <Markdown text={p.readme ?? ''} />
      </Modal>
    </div>
  )
}

function EditAttempt({ attempt, onClose, onSaved }: { attempt: Attempt; onClose: () => void; onSaved: () => void }) {
  const [minutes, setMinutes] = useState((attempt.activeSeconds / 60).toFixed(1))
  const [notes, setNotes] = useState(attempt.notes ?? '')
  const [err, setErr] = useState<string | null>(null)
  async function save() {
    try {
      await api.patch(`/api/attempts/${attempt.id}`, { durationMinutes: Number(minutes), notes })
      onSaved()
    } catch (e: any) { setErr(e.message) }
  }
  return (
    <Modal open onClose={onClose} title="Edit attempt">
      <div className="space-y-3">
        <ErrorBox error={err} />
        <label className="block"><span className="label">Duration (minutes)</span>
          <input className="input mt-1 w-full" type="number" min={0} step={0.5} value={minutes} onChange={e => setMinutes(e.target.value)} /></label>
        <label className="block"><span className="label">Notes</span>
          <textarea className="input mt-1 h-28 w-full" value={notes} onChange={e => setNotes(e.target.value)} placeholder="What tripped you up? What to remember next time?" /></label>
        <div className="flex justify-end gap-2">
          <button className="btn" onClick={onClose}>Cancel</button>
          <button className="btn btn-primary" onClick={save}>Save</button>
        </div>
      </div>
    </Modal>
  )
}
