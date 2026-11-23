import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ProblemRow } from '../lib/api'
import { useApi } from '../lib/useApi'
import { fmtAgo, fmtDue, fmtMinutes } from '../lib/format'
import { DifficultyBadge, ErrorBox, Flags, Spinner, StageBadge, StatusBadge } from '../components/ui'

type SortKey = 'id' | 'attempts' | 'lastTried' | 'best' | 'review'

export default function ProblemsPage() {
  const { data, error, loading } = useApi<ProblemRow[]>('/api/problems')
  const [q, setQ] = useState('')
  const [topic, setTopic] = useState('')
  const [diff, setDiff] = useState('')
  const [status, setStatus] = useState('')
  const [sort, setSort] = useState<SortKey>('id')

  const topics = useMemo(() => [...new Map((data ?? []).map(p => [p.topicNo, p.topic])).entries()], [data])
  const rows = useMemo(() => {
    let r = (data ?? []).filter(p =>
      (!q || `${p.title} ${p.id} ${p.lcNumber}`.toLowerCase().includes(q.toLowerCase())) &&
      (!topic || p.topicNo === topic) && (!diff || p.difficulty === diff) &&
      (!status || (status === 'Solved' ? p.passes > 0 : p.status === status)))
    const by: Record<SortKey, (a: ProblemRow, b: ProblemRow) => number> = {
      id: (a, b) => a.id - b.id,
      attempts: (a, b) => b.attempts - a.attempts,
      lastTried: (a, b) => (b.lastTried ?? '').localeCompare(a.lastTried ?? ''),
      best: (a, b) => (a.bestSeconds ?? 1e12) - (b.bestSeconds ?? 1e12),
      review: (a, b) => (a.nextReviewAt ?? '9999').localeCompare(b.nextReviewAt ?? '9999'),
    }
    return [...r].sort(by[sort])
  }, [data, q, topic, diff, status, sort])

  if (loading && !data) return <Spinner />
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">Problems <span className="text-mute">({rows.length})</span></h1>
      <ErrorBox error={error} />
      <div className="flex flex-wrap gap-2">
        <input className="input w-64" placeholder="Search title, NC or LC number…" value={q} onChange={e => setQ(e.target.value)} />
        <select className="input" value={topic} onChange={e => setTopic(e.target.value)}>
          <option value="">All topics</option>{topics.map(([no, t]) => <option key={no} value={no}>{t}</option>)}
        </select>
        <select className="input" value={diff} onChange={e => setDiff(e.target.value)}>
          <option value="">All difficulties</option><option>Easy</option><option>Medium</option><option>Hard</option>
        </select>
        <select className="input" value={status} onChange={e => setStatus(e.target.value)}>
          <option value="">Any status</option><option value="New">New</option><option value="Solved">Solved at least once</option>
          <option value="Failed">Never solved, attempted</option><option value="InProgress">In progress</option>
        </select>
        <select className="input" value={sort} onChange={e => setSort(e.target.value as SortKey)}>
          <option value="id">Sort: roadmap order</option><option value="attempts">Sort: most attempts</option>
          <option value="lastTried">Sort: last tried</option><option value="best">Sort: best time</option><option value="review">Sort: next review</option>
        </select>
      </div>
      <div className="card overflow-x-auto">
        <table className="grid">
          <thead><tr><th>#</th><th>Problem</th><th>Topic</th><th>Difficulty</th><th>Status</th><th>Attempts</th><th>Best</th><th>Last</th><th>Last tried</th><th>Next review</th></tr></thead>
          <tbody>
            {rows.map(p => (
              <tr key={p.id}>
                <td className="font-mono text-xs text-mute">{String(p.id).padStart(3, '0')}</td>
                <td><Link className="hover:text-accent" to={`/problems/${p.id}`}>{p.title}</Link> <span className="text-xs text-mute">LC {p.lcNumber}</span></td>
                <td className="text-ink2">{p.topic}</td>
                <td><DifficultyBadge d={p.difficulty} /></td>
                <td><div className="flex flex-wrap gap-1"><StatusBadge status={p.status} /><Flags hinted={p.lastHinted} overTime={p.lastOverTime} /></div></td>
                <td className="tabular-nums">{p.attempts ? `${p.passes}/${p.attempts}` : '—'}</td>
                <td className="tabular-nums text-ink2">{fmtMinutes(p.bestSeconds)}</td>
                <td className="tabular-nums text-ink2">{fmtMinutes(p.lastSeconds)}</td>
                <td className="text-ink2">{fmtAgo(p.lastTried)}</td>
                <td className="text-ink2">{p.reviewStage > 0 ? <span className="flex items-center gap-1.5"><StageBadge stage={p.reviewStage} />{fmtDue(p.nextReviewAt)}</span> : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
