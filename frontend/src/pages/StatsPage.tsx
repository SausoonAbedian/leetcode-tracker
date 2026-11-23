import { Link, useSearchParams } from 'react-router-dom'
import { Stats, Topic } from '../lib/api'
import { useApi } from '../lib/useApi'
import { pct } from '../lib/format'
import { Heatmap, TimeBars, UniqueBars } from '../components/Charts'
import { Badge, ErrorBox, Section, Spinner, StatTile } from '../components/ui'

const STRENGTH_TONE: Record<string, 'good' | 'bad' | 'neutral' | 'info'> = { Strong: 'good', Weak: 'bad', Average: 'info' }

export default function StatsPage() {
  const [params, setParams] = useSearchParams()
  const topic = params.get('topic') ?? 'all'
  const topics = useApi<Topic[]>('/api/topics')
  const { data: s, error, loading } = useApi<Stats>(`/api/stats?topic=${topic}`)
  const setTopic = (t: string) => setParams(t === 'all' ? {} : { topic: t })

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-xl font-semibold">Statistics</h1>
        <select className="input ml-auto" value={topic} onChange={e => setTopic(e.target.value)}>
          <option value="all">All topics</option>
          {topics.data?.map(t => <option key={t.topicNo} value={t.topicNo}>{t.topic}</option>)}
        </select>
      </div>
      <ErrorBox error={error} />
      {loading && !s && <Spinner />}
      {s && (
        <>
          <div>
            <h2 className="mb-2 text-sm font-semibold text-ink2">Unique problems</h2>
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
              <StatTile label="Solved" value={s.unique.solved} sub={`${pct(s.unique.total ? s.unique.solved / s.unique.total : 0)} of ${s.unique.total}`} />
              <StatTile label="Attempted, not solved" value={s.unique.attemptedNotSolved} />
              <StatTile label="Not attempted" value={s.unique.untouched} />
              <StatTile label="Total problems" value={s.unique.total} />
            </div>
            <div className="mt-3 card p-4"><UniqueBars unique={s.unique} /></div>
          </div>

          <div>
            <h2 className="mb-2 text-sm font-semibold text-ink2">All attempts (including repeats)</h2>
            <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-6">
              <StatTile label="Attempts" value={s.attempts.total} />
              <StatTile label="Passed" value={s.attempts.passed} sub={`${pct(s.attempts.passRate)} pass rate`} />
              <StatTile label="Failed" value={s.attempts.failed} sub={`${s.attempts.gaveUp} gave up`} />
              <StatTile label="With hints" value={s.attempts.hinted} sub={pct(s.attempts.hintRate)} />
              <StatTile label="Over time" value={s.attempts.overTime} sub={pct(s.attempts.overTimeRate)} />
              <StatTile label="Streak" value={`${s.attempts.currentStreak}d`} sub={`longest ${s.attempts.longestStreak}d`} />
            </div>
            <div className="mt-3 grid gap-4 lg:grid-cols-2">
              <Section title="Average solve time (passed attempts)">
                <TimeBars times={s.attempts.times} />
                <table className="grid mt-2">
                  <thead><tr><th>Difficulty</th><th>Passed</th><th>Average</th><th>Median</th><th>Limit</th></tr></thead>
                  <tbody>{s.attempts.times.map(t => (
                    <tr key={t.difficulty}><td>{t.difficulty}</td><td className="tabular-nums">{t.passed}</td>
                      <td className="tabular-nums">{t.avgMinutes ?? '—'} min</td><td className="tabular-nums">{t.medianMinutes ?? '—'} min</td><td>{t.limitMinutes} min</td></tr>
                  ))}</tbody>
                </table>
              </Section>
              <Section title="Activity (finished attempts per day)"><Heatmap days={s.attempts.heatmap} /></Section>
            </div>
          </div>

          <Section title="By topic" right={<span className="text-xs text-mute">Score = 50% pass rate + 20% no hints + 20% within time + 10% coverage · needs ≥ 2 attempts</span>}>
            <div className="overflow-x-auto">
              <table className="grid">
                <thead><tr><th>Topic</th><th>Solved</th><th>Attempts</th><th>Pass rate</th><th>Hint rate</th><th>Over time</th><th>Avg time</th><th>Score</th><th></th></tr></thead>
                <tbody>
                  {s.topics.map(t => (
                    <tr key={t.topicNo} className={topic === t.topicNo ? 'bg-s2' : ''}>
                      <td><button className="hover:text-accent" onClick={() => setTopic(t.topicNo)}>{t.topic}</button></td>
                      <td className="tabular-nums">
                        <div className="flex items-center gap-2">
                          <div className="h-1.5 w-16 overflow-hidden rounded bg-s3"><div className="h-full rounded" style={{ width: `${(t.solved / Math.max(1, t.problems)) * 100}%`, background: 'var(--good)' }} /></div>
                          {t.solved}/{t.problems}
                        </div>
                      </td>
                      <td className="tabular-nums">{t.attempts}</td>
                      <td className="tabular-nums">{t.attempts ? pct(t.passRate) : '—'}</td>
                      <td className="tabular-nums">{t.attempts ? pct(t.hintRate) : '—'}</td>
                      <td className="tabular-nums">{t.attempts ? pct(t.overTimeRate) : '—'}</td>
                      <td className="tabular-nums">{t.avgMinutes != null ? `${t.avgMinutes} min` : '—'}</td>
                      <td className="tabular-nums">{t.score ?? '—'}</td>
                      <td><div className="flex items-center gap-2">
                        <Badge tone={STRENGTH_TONE[t.strength] ?? 'neutral'}>{t.strength}</Badge>
                        <Link className="text-xs text-accent" to={`/queue?topic=${t.topicNo}`}>queue →</Link>
                      </div></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Section>
        </>
      )}
    </div>
  )
}
