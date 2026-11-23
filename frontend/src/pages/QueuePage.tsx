import { Link, useSearchParams } from 'react-router-dom'
import { QueueView, Topic, tzParam } from '../lib/api'
import { DailyList, DailyProgress } from '../components/DailyPlan'
import { useApi } from '../lib/useApi'
import { fmtDate, fmtIn } from '../lib/format'
import { QueueRow } from '../components/QueueList'
import { DifficultyBadge, ErrorBox, Section, Spinner } from '../components/ui'

export default function QueuePage() {
  const [params, setParams] = useSearchParams()
  const topic = params.get('topic') ?? 'all'
  const difficulty = params.get('difficulty') ?? ''
  const topics = useApi<Topic[]>('/api/topics')
  const q = useApi<QueueView>(`/api/queue?topic=${topic}&difficulty=${difficulty}&${tzParam()}`)

  const set = (k: string, v: string) => {
    const p = new URLSearchParams(params)
    if (v && v !== 'all') p.set(k, v); else p.delete(k)
    setParams(p)
  }
  let rank = 0
  const topicName = topics.data?.find(t => t.topicNo === topic)?.topic

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-xl font-semibold">Queue{topicName ? ` · ${topicName}` : ''}</h1>
        <div className="ml-auto flex gap-2">
          <select className="input" value={difficulty} onChange={e => set('difficulty', e.target.value)}>
            <option value="">All difficulties</option><option>Easy</option><option>Medium</option><option>Hard</option>
          </select>
          <Link className="btn" to="/settings">Priority rules</Link>
        </div>
      </div>

      <div className="flex flex-wrap gap-1.5">
        <button className={`btn btn-sm ${topic === 'all' ? 'btn-primary' : ''}`} onClick={() => set('topic', 'all')}>All topics</button>
        {topics.data?.map(t => (
          <button key={t.topicNo} className={`btn btn-sm ${topic === t.topicNo ? 'btn-primary' : ''}`} onClick={() => set('topic', t.topicNo)}>{t.topic}</button>
        ))}
      </div>

      <ErrorBox error={q.error} />
      {q.loading && !q.data && <Spinner />}
      {q.data && (
        <>
          {q.data.daily && (
            <Section title={<span>Today{topicName || difficulty ? <span className="ml-1 text-mute">(filtered)</span> : null}</span>}
              right={<Link className="text-xs text-accent" to="/settings">Daily plan →</Link>}>
              <DailyProgress plan={q.data.daily} />
              <DailyList plan={q.data.daily} />
            </Section>
          )}
          {q.data.groups.map(g => (
            <Section key={g.rule} title={<span>{g.priority}. {g.label} <span className="ml-1 text-mute">({g.items.length})</span></span>}>
              {g.items.length === 0 ? <p className="text-sm text-mute">Nothing here.</p> : (
                <div className="-mx-3 max-h-[480px] overflow-y-auto">
                  {g.items.map(it => <QueueRow key={it.problemId} item={it} rank={++rank} />)}
                </div>
              )}
            </Section>
          ))}
          {q.data.cooling.length > 0 && (
            <Section title={<span>Cooling down after a fail <span className="text-mute">({q.data.cooling.length})</span></span>}>
              <div className="-mx-3">
                {q.data.cooling.map(c => (
                  <Link key={c.problemId} to={`/problems/${c.problemId}`} className="flex items-center gap-3 rounded-lg px-3 py-2 hover:bg-s2">
                    <span className="w-12 font-mono text-xs text-mute">NC{String(c.problemId).padStart(3, '0')}</span>
                    <span className="flex-1 truncate text-sm">{c.title}</span>
                    <span className="w-20"><DifficultyBadge d={c.difficulty} /></span>
                    <span className="w-40 text-right text-xs text-mute">failed {fmtDate(c.failedAt)}</span>
                    <span className="w-28 text-right text-xs text-ink2">ready {fmtIn(c.readyAt)}</span>
                  </Link>
                ))}
              </div>
            </Section>
          )}
          {q.data.excluded.length > 0 && (
            <p className="text-xs text-mute">{q.data.excluded.length} problem(s) hidden because their rule is turned off in Settings.</p>
          )}
        </>
      )}
    </div>
  )
}
