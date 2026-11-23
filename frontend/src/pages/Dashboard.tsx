import { Link, useNavigate } from 'react-router-dom'
import { api, QueueView, Stats, tzParam } from '../lib/api'
import { DailyList, DailyProgress } from '../components/DailyPlan'
import { useApi } from '../lib/useApi'
import { useActiveAttempt } from '../lib/activeAttempt'
import { pct } from '../lib/format'
import { QueueRow } from '../components/QueueList'
import { DifficultyBadge, ErrorBox, Notice, Section, Spinner, StatTile } from '../components/ui'
import { useState } from 'react'

export default function Dashboard() {
  const queue = useApi<QueueView>(`/api/queue?${tzParam()}`)
  const stats = useApi<Stats>('/api/stats')
  const { active, refresh } = useActiveAttempt()
  const nav = useNavigate()
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (queue.loading && !queue.data) return <Spinner />
  const q = queue.data
  const s = stats.data
  const empty = s && s.unique.total === 0

  const next = q?.next
  async function downloadNext() {
    if (!next) return
    setBusy(true); setErr(null)
    try {
      await api.download(`/api/problems/${next.problemId}/download`)
      await refresh()
      nav(`/problems/${next.problemId}`)
    } catch (e: any) { setErr(e.message) } finally { setBusy(false) }
  }

  return (
    <div className="space-y-5">
      <h1 className="text-xl font-semibold">Dashboard</h1>
      <ErrorBox error={queue.error ?? stats.error ?? err} />
      {empty && (
        <Notice>No problems yet. <Link className="underline" to="/import">Import your DSA project</Link> to fill the database.</Notice>
      )}

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="card p-5 lg:col-span-2">
          <div className="label">{active ? 'Open attempt' : 'Next problem'}</div>
          {active ? (
            <div className="mt-2 flex flex-wrap items-center gap-3">
              <div className="min-w-0 flex-1">
                <div className="text-lg font-semibold">NC{String(active.problemId).padStart(3, '0')} · {active.problemTitle}</div>
                <div className="text-sm text-mute">{active.status === 'Downloaded' ? 'Downloaded, not started yet' : active.status}</div>
              </div>
              <Link className="btn btn-primary" to={`/problems/${active.problemId}`}>Continue →</Link>
            </div>
          ) : next ? (
            <div className="mt-2 flex flex-wrap items-center gap-3">
              <div className="min-w-0 flex-1">
                <div className="text-lg font-semibold">NC{String(next.problemId).padStart(3, '0')} · {next.title}</div>
                <div className="mt-1 flex items-center gap-3 text-sm text-mute">
                  <DifficultyBadge d={next.difficulty} /><span>{next.topic}</span><span>· {next.reason}</span>
                </div>
              </div>
              <Link className="btn" to={`/problems/${next.problemId}`}>Open</Link>
              <button className="btn btn-primary" disabled={busy} onClick={downloadNext}>{busy ? <Spinner /> : '↓'} Download & go</button>
            </div>
          ) : <div className="mt-2 text-sm text-mute">The queue is empty.</div>}
        </div>

        <div className="grid grid-cols-2 gap-3">
          <StatTile label="Unique solved" value={s ? `${s.unique.solved}` : '—'} sub={s ? `of ${s.unique.total} problems` : ''} />
          <StatTile label="Total attempts" value={s?.attempts.total ?? '—'} sub={s ? `${pct(s.attempts.passRate)} passed` : ''} />
          <StatTile label="Streak" value={s ? `${s.attempts.currentStreak}d` : '—'} sub={s ? `best ${s.attempts.longestStreak}d` : ''} />
          <StatTile label="Reviews due" value={q ? (q.groups.find(g => g.rule === 'ReviewDue')?.items.length ?? 0) : '—'}
            sub={q ? `${q.groups.find(g => g.rule === 'ReviewUpcoming')?.items.length ?? 0} upcoming · ${q.cooling.length} cooling` : ''} />
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        {q?.daily ? (
          <Section className="lg:col-span-2" title="Today" right={<Link className="text-xs text-accent" to="/queue">Full queue →</Link>}>
            <DailyProgress plan={q.daily} />
            <DailyList plan={q.daily} />
          </Section>
        ) : (
          <Section className="lg:col-span-2" title="Up next" right={<Link className="text-xs text-accent" to="/queue">Full queue →</Link>}>
            <div className="-mx-3">
              {q?.groups.flatMap(g => g.items).slice(0, 10).map((it, i) => <QueueRow key={it.problemId} item={it} rank={i + 1} />)}
            </div>
          </Section>
        )}
        <div className="space-y-4">
          <Section title="Weak topics" right={<Link className="text-xs text-accent" to="/stats">Stats →</Link>}>
            {s?.weak.length ? s.weak.map(t => (
              <Link key={t.topicNo} to={`/queue?topic=${t.topicNo}`} className="flex items-center justify-between rounded px-2 py-1.5 text-sm hover:bg-s2">
                <span>{t.topic}</span><span className="text-xs text-mute">score {t.score} · {pct(t.passRate)} pass</span>
              </Link>
            )) : <p className="text-sm text-mute">Needs at least 2 attempts in a topic.</p>}
          </Section>
          <Section title="Strong topics">
            {s?.strong.length ? s.strong.map(t => (
              <Link key={t.topicNo} to={`/queue?topic=${t.topicNo}`} className="flex items-center justify-between rounded px-2 py-1.5 text-sm hover:bg-s2">
                <span>{t.topic}</span><span className="text-xs text-mute">score {t.score}</span>
              </Link>
            )) : <p className="text-sm text-mute">Nothing yet — keep going.</p>}
          </Section>
        </div>
      </div>
    </div>
  )
}
