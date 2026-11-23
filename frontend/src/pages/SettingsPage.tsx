import { useEffect, useState } from 'react'
import { api, ProviderStatus, Settings } from '../lib/api'
import { useApi } from '../lib/useApi'
import { Badge, ErrorBox, Notice, Section, Spinner } from '../components/ui'

const RULE_LABEL: Record<string, string> = {
  New: 'New — never attempted',
  FailedReady: 'Failed — after the cooldown',
  Hinted: 'Solved with hints',
  OverTime: 'Solved, but over the time limit',
  ReviewDue: 'Review due (spaced repetition)',
  ReviewUpcoming: 'Upcoming reviews (not due yet)',
}

export default function SettingsPage() {
  const remote = useApi<Settings>('/api/settings')
  const providers = useApi<ProviderStatus[]>('/api/ai/providers')
  const [s, setS] = useState<Settings | null>(null)
  const [saved, setSaved] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [drag, setDrag] = useState<number | null>(null)

  useEffect(() => { if (remote.data) setS(structuredClone(remote.data)) }, [remote.data])
  if (!s) return remote.error ? <ErrorBox error={remote.error} /> : <Spinner />

  const update = (fn: (x: Settings) => void) => { const c = structuredClone(s); fn(c); setS(c); setSaved(false) }
  const move = (from: number, to: number) => update(x => { const [r] = x.rules.splice(from, 1); x.rules.splice(to, 0, r) })

  async function save() {
    setErr(null)
    try {
      const res = await api.put<Settings>('/api/settings', s)
      setS(res); setSaved(true); providers.reload()
    } catch (e: any) { setErr(e.message) }
  }

  return (
    <div className="max-w-3xl space-y-4">
      <div className="flex items-center gap-3">
        <h1 className="text-xl font-semibold">Settings</h1>
        <button className="btn btn-primary ml-auto" onClick={save}>Save</button>
      </div>
      <ErrorBox error={err} />
      {saved && <Notice tone="good">Saved. The queue updates right away.</Notice>}

      <Section title="Queue priority" right={<span className="text-xs text-mute">Drag or use the arrows. A problem goes into the first enabled rule it matches.</span>}>
        <ol className="space-y-1.5">
          {s.rules.map((r, i) => (
            <li key={r.rule} draggable onDragStart={() => setDrag(i)} onDragOver={e => e.preventDefault()}
              onDrop={() => { if (drag != null && drag !== i) move(drag, i); setDrag(null) }}
              className={`flex items-center gap-3 rounded-lg border border-line bg-s0 px-3 py-2 ${drag === i ? 'opacity-50' : ''} ${r.enabled ? '' : 'opacity-60'}`}>
              <span className="cursor-grab text-mute">⋮⋮</span>
              <span className="w-5 font-mono text-sm text-mute">{i + 1}</span>
              <span className="flex-1 text-sm">{RULE_LABEL[r.rule] ?? r.rule}</span>
              <button className="btn btn-sm" disabled={i === 0} onClick={() => move(i, i - 1)}>↑</button>
              <button className="btn btn-sm" disabled={i === s.rules.length - 1} onClick={() => move(i, i + 1)}>↓</button>
              <label className="flex items-center gap-1.5 text-xs text-ink2">
                <input type="checkbox" checked={r.enabled} onChange={e => update(x => { x.rules[i].enabled = e.target.checked })} /> on
              </label>
            </li>
          ))}
        </ol>
        <div className="mt-4 flex items-center gap-3">
          <label className="text-sm text-ink2">Retry a failed problem after</label>
          <input className="input w-24" type="number" min={0} step={1} value={s.failCooldownHours}
            onChange={e => update(x => { x.failCooldownHours = Number(e.target.value) })} />
          <span className="text-sm text-mute">hours</span>
        </div>
      </Section>

      <Section title="Review stages" right={<span className="text-xs text-mute">A hint, a fail or going over time resets to the start.</span>}>
        <p className="mb-3 text-sm text-ink2">Days to wait before reviewing a problem after each clean pass in a row. The last value repeats.</p>
        <div className="flex flex-wrap items-center gap-3">
          {s.reviewIntervalsDays.map((d, i) => (
            <label key={i} className="flex items-center gap-2 text-sm">
              <span className="text-mute">{i === s.reviewIntervalsDays.length - 1 ? `Pass ${i + 1}+` : `Pass ${i + 1}`}</span>
              <input className="input w-20" type="number" min={0} step={1} value={d}
                onChange={e => update(x => { x.reviewIntervalsDays[i] = Number(e.target.value) })} />
              <span className="text-mute">days</span>
              {s.reviewIntervalsDays.length > 1 && (
                <button className="text-mute hover:text-ink" title="Remove stage" onClick={() => update(x => { x.reviewIntervalsDays.splice(i, 1) })}>✕</button>
              )}
            </label>
          ))}
          {s.reviewIntervalsDays.length < 10 && (
            <button className="btn btn-sm" onClick={() => update(x => { x.reviewIntervalsDays.push(x.reviewIntervalsDays[x.reviewIntervalsDays.length - 1] ?? 8) })}>+ Add stage</button>
          )}
        </div>
      </Section>

      <Section title="Time limits" right={<span className="text-xs text-mute">Going over is a flag, not a failure.</span>}>
        <div className="flex flex-wrap gap-4">
          {['Easy', 'Medium', 'Hard'].map(d => (
            <label key={d} className="flex items-center gap-2 text-sm">
              <span className="w-16 text-ink2">{d}</span>
              <input className="input w-20" type="number" min={0} value={s.timeLimits[d] ?? 45}
                onChange={e => update(x => { x.timeLimits[d] = Number(e.target.value) })} />
              <span className="text-mute">min</span>
            </label>
          ))}
        </div>
      </Section>

      <Section title="AI for hints and reviews">
        <div className="space-y-2">
          {providers.data?.map(p => (
            <label key={p.id} className="flex items-center gap-3 rounded-lg border border-line bg-s0 px-3 py-2">
              <input type="radio" name="provider" checked={s.ai.provider === p.id} onChange={() => update(x => { x.ai.provider = p.id })} />
              <span className="flex-1 text-sm">{p.name}</span>
              {p.configured ? <Badge tone="good">configured</Badge> : <Badge tone="warn" title={p.hint}>not configured</Badge>}
            </label>
          ))}
          {providers.data?.find(p => p.id === s.ai.provider && !p.configured) && (
            <Notice tone="warn">{providers.data.find(p => p.id === s.ai.provider)!.hint}, then restart with <code className="font-mono">docker compose up -d</code>.</Notice>
          )}
        </div>
        <div className="mt-4 grid gap-3 sm:grid-cols-3">
          <label className="block"><span className="label">Anthropic model</span>
            <input className="input mt-1 w-full" value={s.ai.anthropicModel} onChange={e => update(x => { x.ai.anthropicModel = e.target.value })} /></label>
          <label className="block"><span className="label">DeepSeek model</span>
            <select className="input mt-1 w-full" value={s.ai.deepSeekModel} onChange={e => update(x => { x.ai.deepSeekModel = e.target.value })}>
              <option value="deepseek-chat">deepseek-chat</option><option value="deepseek-reasoner">deepseek-reasoner</option>
            </select></label>
          <label className="block"><span className="label">Claude Code model</span>
            <input className="input mt-1 w-full" placeholder="CLI default" value={s.ai.claudeCodeModel} onChange={e => update(x => { x.ai.claudeCodeModel = e.target.value })} /></label>
        </div>
      </Section>
    </div>
  )
}
