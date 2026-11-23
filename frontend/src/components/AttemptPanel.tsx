import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AiAnswer, api, ApiError, Attempt, ProblemDetail, TestRun } from '../lib/api'
import { liveSeconds, useActiveAttempt } from '../lib/activeAttempt'
import { FolderSource, folderKey, forgetFolder, pickFolder, readSolution, rememberedFolder, rememberedFolderName, supportsHandles, zipFolder } from '../lib/folder'
import { fmtDate, fmtDuration, toLocalInput } from '../lib/format'
import { TestResults } from './TestResults'
import { Badge, ErrorBox, Flags, Markdown, Modal, Notice, Spinner, StatusBadge, useTick } from './ui'

interface SubmitResult { attempt: Attempt; run: TestRun; warnings: string[]; message: string; reviewStage: number; nextReviewAt?: string | null }

export function AttemptPanel({ problem, onChanged }: { problem: ProblemDetail; onChanged: () => void }) {
  const { active, fetchedAt, refresh } = useActiveAttempt()
  const mine = active && active.problemId === problem.id ? active : null
  const other = active && active.problemId !== problem.id ? active : null
  useTick(1000, mine?.status === 'Running')

  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [folderName, setFolderName] = useState<string | null>(null)
  const [lastRun, setLastRun] = useState<{ run: TestRun; warnings: string[] } | null>(null)
  const [submitted, setSubmitted] = useState<SubmitResult | null>(null)
  const [hint, setHint] = useState<AiAnswer | null>(null)
  const [startAt, setStartAt] = useState<string>('')
  const [editStart, setEditStart] = useState(false)
  const [confirmGiveUp, setConfirmGiveUp] = useState(false)

  const key = folderKey(problem.id)
  useEffect(() => { rememberedFolderName(key).then(setFolderName) }, [key])
  useEffect(() => { setLastRun(null); setSubmitted(null); setHint(null) }, [problem.id])

  async function act<T>(label: string, fn: () => Promise<T>): Promise<T | undefined> {
    setBusy(label); setError(null)
    try { return await fn() }
    catch (e) { setError(e instanceof ApiError || e instanceof Error ? e.message : String(e)) }
    finally { setBusy(null) }
  }
  const after = async () => { await refresh(); onChanged() }

  /** The folder to read: remembered one (Chrome/Edge), else ask. */
  async function getFolder(): Promise<FolderSource | null> {
    const remembered = await rememberedFolder(key)
    if (remembered) return remembered
    const picked = await pickFolder(key)
    if (picked) setFolderName(supportsHandles() ? picked.name : null)
    return picked
  }

  async function chooseFolder() {
    const picked = await pickFolder(key)
    if (picked) setFolderName(supportsHandles() ? picked.name : picked.name)
  }

  const download = () => act('download', async () => {
    await api.download(`/api/problems/${problem.id}/download`)
    await after()
  })

  const start = () => act('start', async () => {
    await api.post(`/api/attempts/${mine!.id}/start`, { startedAt: startAt ? new Date(startAt).toISOString() : null })
    setStartAt('')
    await after()
  })

  const saveStart = () => act('editStart', async () => {
    await api.patch(`/api/attempts/${mine!.id}`, { startedAt: new Date(startAt).toISOString() })
    setEditStart(false); setStartAt('')
    await after()
  })

  const pauseResume = () => act('pause', async () => {
    await api.post(`/api/attempts/${mine!.id}/${mine!.status === 'Paused' ? 'resume' : 'pause'}`)
    await refresh()
  })

  const runTests = () => act('run', async () => {
    const folder = await getFolder()
    if (!folder) return
    setBusy('run-zip')
    const { blob } = await zipFolder(folder)
    setBusy('run')
    setLastRun(await api.upload<{ run: TestRun; warnings: string[] }>(`/api/problems/${problem.id}/run`, blob))
  })

  const submit = () => act('submit', async () => {
    const folder = await getFolder()
    if (!folder) return
    const { blob } = await zipFolder(folder)
    const res = await api.upload<SubmitResult>(`/api/attempts/${mine!.id}/submit`, blob)
    setSubmitted(res); setLastRun(null)
    await forgetFolder(key); setFolderName(null)
    await after()
  })

  const giveUp = (withCode: boolean) => act('giveup', async () => {
    setConfirmGiveUp(false)
    let blob: Blob | undefined
    if (withCode) {
      const folder = await getFolder()
      if (folder) blob = (await zipFolder(folder)).blob
    }
    if (blob) await api.upload(`/api/attempts/${mine!.id}/giveup`, blob)
    else await api.post(`/api/attempts/${mine!.id}/giveup`)
    await forgetFolder(key); setFolderName(null)
    await after()
  })

  const discard = (id: number) => act('discard', async () => {
    await api.post(`/api/attempts/${id}/discard`)
    await after()
  })

  const askHint = (level: number) => act(`hint${level}`, async () => {
    let code: string | null = null
    const folder = await rememberedFolder(key)
    if (folder) code = await readSolution(folder, problem.relPath)
    const failing = lastRun?.run.tests.filter(t => t.outcome !== 'Passed').map(t => t.name) ?? []
    setHint(await api.post<AiAnswer>('/api/ai/hint', { problemId: problem.id, level, code, failingTests: failing }))
    await after()
  })

  const reviewCurrent = () => act('review', async () => {
    const folder = await getFolder()
    if (!folder) return
    const code = await readSolution(folder, problem.relPath)
    if (!code) throw new Error(`Couldn't find ${problem.relPath}/Solution.cs in "${folder.name}".`)
    const summary = lastRun ? `${lastRun.run.passedCount}/${lastRun.run.total} tests passed${lastRun.run.buildFailed ? ' (build failed)' : ''}` : undefined
    setHint(await api.post<AiAnswer>('/api/ai/review', { problemId: problem.id, code, testSummary: summary }))
    onChanged()
  })

  // ---------- render ----------
  const secs = mine ? liveSeconds(mine, fetchedAt) : 0
  const limit = problem.limitMinutes * 60
  const over = mine?.startedAt != null && secs > limit
  const remaining = limit - secs

  return (
    <div className="card space-y-4 p-4">
      <div className="flex items-center gap-2">
        <h2 className="text-sm font-semibold">Attempt</h2>
        {mine && <StatusBadge status={mine.status} />}
        {mine && <Flags hinted={mine.hinted} hints={mine.hintsUsed} overTime={over} />}
        <span className="ml-auto text-xs text-mute">Time limit {problem.limitMinutes} min ({problem.difficulty})</span>
      </div>

      <ErrorBox error={error} onClose={() => setError(null)} />

      {submitted && (
        <div className="space-y-3">
          <Notice tone={submitted.attempt.status === 'Passed' ? 'good' : 'warn'}>
            <strong>{submitted.attempt.status === 'Passed' ? 'Accepted.' : 'Not accepted.'}</strong>{' '}
            Attempt finished in {fmtDuration(submitted.attempt.activeSeconds)}
            {submitted.attempt.overTime && ' (over the time limit)'}
            {submitted.attempt.hinted && ` with ${submitted.attempt.hintsUsed} hint${submitted.attempt.hintsUsed > 1 ? 's' : ''}`}.
            {' '}{submitted.reviewStage > 0 && submitted.nextReviewAt
              ? <>Review stage {submitted.reviewStage}: next review {fmtDate(submitted.nextReviewAt)} (in {Math.round((new Date(submitted.nextReviewAt).getTime() - Date.now()) / 86400000)} days).</>
              : submitted.attempt.status === 'Passed' ? <>Review stages reset (hint or over time), so it stays in your queue.</> : null}
            {' '}You can delete the local <code className="font-mono">{problem.folder}</code> folder now.
          </Notice>
          <TestResults run={submitted.run} warnings={submitted.warnings} saved />
        </div>
      )}

      {other && (
        <Notice tone="warn">
          <Link className="underline" to={`/problems/${other.problemId}`}>NC{String(other.problemId).padStart(3, '0')} {other.problemTitle}</Link> is still open
          ({other.status.toLowerCase()}). Finish it first, or{' '}
          {other.status === 'Downloaded'
            ? <button className="underline" onClick={() => discard(other.id)}>discard it</button>
            : <>open it and give up</>}.
        </Notice>
      )}

      {!mine && !other && (
        <div className="flex flex-wrap items-center gap-3">
          <button className="btn btn-primary" disabled={!!busy} onClick={download}>
            {busy === 'download' ? <Spinner /> : '↓'} Download exercise
          </button>
          <span className="text-sm text-mute">Downloads <code className="font-mono">{problem.folder}.zip</code> with a clean stub. The timer starts when you click Start.</span>
        </div>
      )}

      {mine?.status === 'Downloaded' && (
        <div className="space-y-3">
          <p className="text-sm text-ink2">Unzip <code className="font-mono">{problem.folder}.zip</code> anywhere and open it in your IDE. Click <strong>Start</strong> when you begin.</p>
          <div className="flex flex-wrap items-end gap-3">
            <button className="btn btn-good" disabled={!!busy} onClick={start}>{busy === 'start' ? <Spinner /> : '▶'} Start{startAt ? ' at chosen time' : ' now'}</button>
            <label className="flex flex-col gap-1">
              <span className="label">I already started at (optional)</span>
              <input type="datetime-local" className="input" value={startAt} max={toLocalInput(new Date())}
                onChange={e => setStartAt(e.target.value)} />
            </label>
            <button className="btn" disabled={!!busy} onClick={download}>↓ Download again</button>
            <button className="btn btn-danger" disabled={!!busy} onClick={() => discard(mine.id)}>Discard</button>
          </div>
        </div>
      )}

      {(mine?.status === 'Running' || mine?.status === 'Paused') && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-4">
            <div>
              <div className="font-mono text-4xl font-semibold tabular-nums" style={{ color: over ? 'var(--warning)' : 'var(--text-primary)' }}>
                {fmtDuration(secs)}
              </div>
              <div className="mt-1 text-xs" style={{ color: over ? 'var(--warning)' : 'var(--text-muted)' }}>
                {over ? `⏱ ${fmtDuration(-remaining)} over the ${problem.limitMinutes} min limit` : `${fmtDuration(remaining)} left of ${problem.limitMinutes} min`}
              </div>
              <div className="mt-2 h-1.5 w-56 overflow-hidden rounded bg-s3">
                <div className="h-full rounded" style={{ width: `${Math.min(100, (secs / limit) * 100)}%`, background: over ? 'var(--warning)' : 'var(--accent)' }} />
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              <button className="btn" disabled={!!busy} onClick={pauseResume}>{mine.status === 'Paused' ? '▶ Resume' : '❚❚ Pause'}</button>
              <button className="btn btn-sm" onClick={() => { setEditStart(true); setStartAt(toLocalInput(new Date(mine.startedAt!))) }}>Edit start time</button>
              <button className="btn btn-sm" onClick={download}>↓ Download again</button>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2 rounded-lg border border-line bg-s0 px-3 py-2 text-sm">
            <span className="text-mute">Folder:</span>
            {folderName ? <span className="font-mono">{folderName}</span> : <span className="text-mute">{supportsHandles() ? 'not selected yet' : 'you will be asked each time'}</span>}
            <button className="btn btn-sm ml-auto" onClick={chooseFolder}>{folderName ? 'Change' : 'Select folder'}</button>
          </div>

          <div className="flex flex-wrap gap-2">
            <button className="btn" disabled={!!busy} onClick={runTests}>{busy?.startsWith('run') ? <Spinner /> : '⚙'} Run tests</button>
            <button className="btn btn-good" disabled={!!busy} onClick={submit}>{busy === 'submit' ? <Spinner /> : '⬆'} Submit</button>
            <span className="mx-1 w-px bg-line" />
            {[1, 2, 3].map(l => (
              <button key={l} className="btn" disabled={!!busy} onClick={() => askHint(l)} title={['Small nudge', 'Approach', 'Pseudo-code'][l - 1]}>
                {busy === `hint${l}` ? <Spinner /> : '💡'} Hint {l}
              </button>
            ))}
            <button className="btn" disabled={!!busy} onClick={reviewCurrent}>{busy === 'review' ? <Spinner /> : '🔍'} Review</button>
            <button className="btn btn-danger ml-auto" disabled={!!busy} onClick={() => setConfirmGiveUp(true)}>Give up</button>
          </div>
          {busy === 'run' && <p className="text-xs text-mute">Building and running the tests… the first run of a problem can take ~30 seconds.</p>}
          {busy === 'submit' && <p className="text-xs text-mute">Judging your submission…</p>}
          {lastRun && <TestResults run={lastRun.run} warnings={lastRun.warnings} />}
        </div>
      )}

      {hint && (
        <div className="rounded-lg border border-[#9085e955] bg-[#9085e90f] p-3">
          <div className="mb-1 flex items-center gap-2 text-xs text-mute">
            <Badge tone="violet">{hint.kind === 'Review' ? '🔍 Review' : `💡 ${hint.kind.replace('Hint', 'Hint ')}`}</Badge>
            <span>{hint.provider} · {hint.model}</span>
            <button className="ml-auto hover:text-ink" onClick={() => setHint(null)}>✕</button>
          </div>
          <Markdown text={hint.text} />
        </div>
      )}

      <Modal open={editStart} onClose={() => setEditStart(false)} title="Edit start time">
        <div className="space-y-3">
          <input type="datetime-local" className="input w-full" value={startAt} max={toLocalInput(new Date())} onChange={e => setStartAt(e.target.value)} />
          <div className="flex justify-end gap-2">
            <button className="btn" onClick={() => setEditStart(false)}>Cancel</button>
            <button className="btn btn-primary" disabled={!startAt || !!busy} onClick={saveStart}>Save</button>
          </div>
        </div>
      </Modal>

      <Modal open={confirmGiveUp} onClose={() => setConfirmGiveUp(false)} title="Give up on this attempt?">
        <p className="mb-4 text-sm text-ink2">It will be recorded as failed and come back to your queue after the cooldown. Save your current code with it?</p>
        <div className="flex justify-end gap-2">
          <button className="btn" onClick={() => setConfirmGiveUp(false)}>Cancel</button>
          <button className="btn btn-danger" onClick={() => giveUp(false)}>Give up</button>
          <button className="btn btn-danger" onClick={() => giveUp(true)}>Give up & save code</button>
        </div>
      </Modal>
    </div>
  )
}
