import { Bar, BarChart, CartesianGrid, Cell, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { Attempt, Stats } from '../lib/api'
import { fmtDuration } from '../lib/format'
import { DIFF_COLOR } from './ui'

const axis = { stroke: 'var(--text-muted)', fontSize: 11, tickLine: false, axisLine: false }
const grid = <CartesianGrid stroke="#2d2d2a" vertical={false} />

function TipBox({ children }: { children: React.ReactNode }) {
  return <div className="rounded-lg border border-line bg-s2 px-3 py-2 text-xs text-ink2 shadow-lg">{children}</div>
}

function LegendSwatch({ color, label, shape = 'square' }: { color: string; label: string; shape?: 'square' | 'line' }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-ink2">
      {shape === 'square'
        ? <span className="inline-block size-2.5 rounded-sm" style={{ background: color }} />
        : <span className="inline-block w-4 border-t-2 border-dashed" style={{ borderColor: color }} />}
      {label}
    </span>
  )
}

/** One bar per finished attempt (oldest → newest), height = minutes, color = result. */
export function AttemptsChart({ attempts, limitMinutes }: { attempts: Attempt[]; limitMinutes: number }) {
  const data = attempts
    .filter(a => a.status === 'Passed' || a.status === 'Failed')
    .sort((a, b) => new Date(a.endedAt!).getTime() - new Date(b.endedAt!).getTime())
    .map((a, i) => ({
      i: `#${i + 1}`, minutes: +(a.activeSeconds / 60).toFixed(1), a,
      date: new Date(a.endedAt!).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    }))
  if (data.length === 0) return <p className="text-sm text-mute">No finished attempts yet — the chart fills in as you practise this problem.</p>
  const top = Math.max(limitMinutes, ...data.map(d => d.minutes)) * 1.1
  const step = Math.max(limitMinutes > 40 ? 15 : 10, Math.ceil(top / 6 / 5) * 5)
  const yMax = Math.ceil(top / step) * step
  const ticks = Array.from({ length: yMax / step + 1 }, (_, i) => i * step)

  const Flag = (props: any) => {
    const { x, y, width, index } = props
    const a: Attempt = data[index].a
    const glyphs = `${a.hinted ? '💡' : ''}${a.overTime ? '⏱' : ''}`
    return glyphs ? <text x={x + width / 2} y={y - 6} textAnchor="middle" fontSize={11}>{glyphs}</text> : null
  }

  return (
    <div>
      <div className="mb-2 flex flex-wrap gap-4">
        <LegendSwatch color="var(--good)" label="Passed" />
        <LegendSwatch color="var(--critical)" label="Failed / gave up" />
        <LegendSwatch color="var(--warning)" label={`Time limit (${limitMinutes} min)`} shape="line" />
        <span className="text-xs text-ink2">💡 hints used · ⏱ over time</span>
      </div>
      <ResponsiveContainer width="100%" height={220}>
        <BarChart data={data} margin={{ top: 20, right: 8, left: -12, bottom: 0 }} barCategoryGap="25%">
          {grid}
          <XAxis dataKey="i" {...axis} />
          <YAxis {...axis} unit="m" domain={[0, yMax]} ticks={ticks} />
          <ReferenceLine y={limitMinutes} stroke="var(--warning)" strokeDasharray="5 4" strokeWidth={1.5} />
          <Tooltip cursor={{ fill: '#ffffff08' }} content={({ active, payload }) => {
            if (!active || !payload?.length) return null
            const d = payload[0].payload
            const a: Attempt = d.a
            return (
              <TipBox>
                <div className="font-medium text-ink">Attempt {d.i} · {d.date}</div>
                <div>{a.status === 'Passed' ? '✓ Passed' : a.failReason === 'GaveUp' ? '✕ Gave up' : '✕ Failed'} in {fmtDuration(a.activeSeconds)}</div>
                {a.testsTotal != null && <div>{a.testsPassed}/{a.testsTotal} tests</div>}
                {a.hinted && <div>💡 {a.hintsUsed} hint{a.hintsUsed > 1 ? 's' : ''}</div>}
                {a.overTime && <div>⏱ over the {a.limitMinutes} min limit</div>}
              </TipBox>
            )
          }} />
          <Bar dataKey="minutes" radius={[4, 4, 0, 0]} maxBarSize={44} label={<Flag />} isAnimationActive={false}>
            {data.map(d => <Cell key={d.i} fill={d.a.status === 'Passed' ? 'var(--good)' : 'var(--critical)'} />)}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}

/** Solved / attempted / untouched per difficulty as a horizontal stacked bar per row. */
export function UniqueBars({ unique }: { unique: Stats['unique'] }) {
  return (
    <div className="space-y-3">
      {unique.byDifficulty.map(d => {
        const seg = (n: number) => (d.total ? (n / d.total) * 100 : 0)
        return (
          <div key={d.difficulty}>
            <div className="mb-1 flex items-center justify-between text-xs">
              <span className="inline-flex items-center gap-1.5 text-ink2">
                <span className="inline-block size-2 rounded-full" style={{ background: DIFF_COLOR[d.difficulty] }} />{d.difficulty}
              </span>
              <span className="tabular-nums text-mute">{d.solved} / {d.total} solved</span>
            </div>
            <div className="flex h-3 gap-[2px] overflow-hidden rounded" title={`${d.solved} solved · ${d.attemptedNotSolved} attempted, not solved · ${d.untouched} untouched`}>
              {d.solved > 0 && <div style={{ width: `${seg(d.solved)}%`, background: 'var(--good)' }} className="rounded-l" />}
              {d.attemptedNotSolved > 0 && <div style={{ width: `${seg(d.attemptedNotSolved)}%`, background: 'var(--critical)' }} />}
              <div style={{ flex: 1, background: 'var(--surface-3)' }} className="rounded-r" />
            </div>
          </div>
        )
      })}
      <div className="flex flex-wrap gap-4 pt-1">
        <LegendSwatch color="var(--good)" label="Solved" />
        <LegendSwatch color="var(--critical)" label="Attempted, not solved yet" />
        <LegendSwatch color="var(--surface-3)" label="Not attempted" />
      </div>
    </div>
  )
}

/** Average solve time per difficulty against its limit. */
export function TimeBars({ times }: { times: Stats['attempts']['times'] }) {
  const data = times.map(t => ({ ...t, avg: t.avgMinutes ?? 0 }))
  return (
    <ResponsiveContainer width="100%" height={200}>
      <BarChart data={data} margin={{ top: 10, right: 8, left: -12, bottom: 0 }} barCategoryGap="35%">
        {grid}
        <XAxis dataKey="difficulty" {...axis} />
        <YAxis {...axis} unit="m" allowDecimals={false} />
        <Tooltip cursor={{ fill: '#ffffff08' }} content={({ active, payload }) => {
          if (!active || !payload?.length) return null
          const t = payload[0].payload
          return (
            <TipBox>
              <div className="font-medium text-ink">{t.difficulty}</div>
              <div>Average {t.avgMinutes ?? '—'} min · median {t.medianMinutes ?? '—'} min</div>
              <div>{t.passed} passed attempt{t.passed === 1 ? '' : 's'} · limit {t.limitMinutes} min</div>
            </TipBox>
          )
        }} />
        <Bar dataKey="avg" radius={[4, 4, 0, 0]} maxBarSize={56} isAnimationActive={false}>
          {data.map(d => <Cell key={d.difficulty} fill={DIFF_COLOR[d.difficulty]} />)}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

/** GitHub-style calendar of finished attempts per day (last ~53 weeks). Sequential blue, darker = fewer. */
export function Heatmap({ days }: { days: Stats['attempts']['heatmap'] }) {
  const max = Math.max(1, ...days.map(d => d.attempts))
  const color = (n: number) => {
    if (n === 0) return 'var(--seq-0)'
    const step = Math.min(5, Math.ceil((n / max) * 5))
    return `var(--seq-${step})`
  }
  // Align to weeks starting Sunday.
  const first = new Date(days[0].day + 'T00:00:00')
  const pad = first.getDay()
  const cells: (typeof days[number] | null)[] = [...Array(pad).fill(null), ...days]
  const weeks: (typeof days[number] | null)[][] = []
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7))
  return (
    <div className="overflow-x-auto">
      <div className="flex gap-[3px]">
        {weeks.map((w, wi) => (
          <div key={wi} className="flex flex-col gap-[3px]">
            {w.map((d, di) => d
              ? <div key={di} className="size-[11px] rounded-[2px]" style={{ background: color(d.attempts) }}
                  title={`${new Date(d.day + 'T00:00:00').toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' })}: ${d.attempts} attempt${d.attempts === 1 ? '' : 's'}, ${d.passed} passed`} />
              : <div key={di} className="size-[11px]" />)}
          </div>
        ))}
      </div>
      <div className="mt-2 flex items-center gap-1 text-[11px] text-mute">
        Less {[0, 1, 2, 3, 4, 5].map(s => <span key={s} className="inline-block size-[11px] rounded-[2px]" style={{ background: `var(--seq-${s})` }} />)} More
      </div>
    </div>
  )
}
