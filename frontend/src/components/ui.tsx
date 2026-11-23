import { ReactNode, useEffect, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import type { Difficulty } from '../lib/api'

export const DIFF_COLOR: Record<string, string> = { Easy: 'var(--series-1)', Medium: 'var(--series-2)', Hard: 'var(--series-3)' }

export function DifficultyBadge({ d }: { d: Difficulty | string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-ink2">
      <span className="inline-block size-2 rounded-full" style={{ background: DIFF_COLOR[d] }} />
      {d}
    </span>
  )
}

type Tone = 'good' | 'bad' | 'warn' | 'info' | 'neutral' | 'violet'
const TONES: Record<Tone, string> = {
  good: 'bg-[#0ca30c22] text-[#6fd66f] border-[#0ca30c55]',
  bad: 'bg-[#d03b3b22] text-[#f08a8a] border-[#d03b3b55]',
  warn: 'bg-[#fab21920] text-[#fcd06d] border-[#fab21955]',
  info: 'bg-[#3987e522] text-[#86b6ef] border-[#3987e555]',
  violet: 'bg-[#9085e922] text-[#bdb5f5] border-[#9085e955]',
  neutral: 'bg-s2 text-ink2 border-line',
}

export function Badge({ tone = 'neutral', children, title }: { tone?: Tone; children: ReactNode; title?: string }) {
  return <span title={title} className={`inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-[11px] font-medium ${TONES[tone]}`}>{children}</span>
}

export function StatusBadge({ status, failReason }: { status: string; failReason?: string | null }) {
  switch (status) {
    case 'Passed': return <Badge tone="good">✓ Passed</Badge>
    case 'Failed': return <Badge tone="bad">✕ {failReason === 'GaveUp' ? 'Gave up' : 'Failed'}</Badge>
    case 'Running': return <Badge tone="info">● Running</Badge>
    case 'Paused': return <Badge tone="warn">❚❚ Paused</Badge>
    case 'Downloaded': return <Badge tone="neutral">↓ Downloaded</Badge>
    case 'Solved': return <Badge tone="good">✓ Solved</Badge>
    case 'SolvedBeforeFailedLast': return <Badge tone="warn" >✓ Solved · last failed</Badge>
    case 'InProgress': return <Badge tone="info">● In progress</Badge>
    case 'New': return <Badge>New</Badge>
    default: return <Badge>{status}</Badge>
  }
}

export function Flags({ hinted, overTime, hints }: { hinted?: boolean; overTime?: boolean; hints?: number }) {
  return (
    <>
      {hinted && <Badge tone="violet" title="Hints used">💡 {hints ? `${hints} hint${hints > 1 ? 's' : ''}` : 'Hinted'}</Badge>}
      {overTime && <Badge tone="warn" title="Took longer than the time limit">⏱ Over time</Badge>}
    </>
  )
}

/** Spaced-repetition stage: S1, S2, S3+ (blue, stronger with each stage). */
export function StageBadge({ stage, title }: { stage: number; title?: string }) {
  if (!stage) return null
  return <Badge tone="info" title={title ?? `Review stage ${stage}: ${stage} clean pass${stage > 1 ? 'es' : ''} in a row`}>{stage >= 3 ? 'S3+' : `S${stage}`}</Badge>
}

export function Markdown({ text }: { text: string }) {
  return <div className="markdown"><ReactMarkdown remarkPlugins={[remarkGfm]}>{text}</ReactMarkdown></div>
}

export function Spinner({ className = '' }: { className?: string }) {
  return <span className={`inline-block size-4 animate-spin rounded-full border-2 border-ink2/30 border-t-ink2 ${className}`} />
}

export function ErrorBox({ error, onClose }: { error: string | null; onClose?: () => void }) {
  if (!error) return null
  return (
    <div className="flex items-start gap-3 rounded-lg border border-[#d03b3b66] bg-[#d03b3b18] px-3 py-2 text-sm text-[#f3b0b0]">
      <span className="mt-0.5">⚠</span><span className="flex-1 whitespace-pre-wrap">{error}</span>
      {onClose && <button className="text-mute hover:text-ink" onClick={onClose}>✕</button>}
    </div>
  )
}

export function Notice({ children, tone = 'info' }: { children: ReactNode; tone?: 'info' | 'good' | 'warn' }) {
  const cls = tone === 'good' ? 'border-[#0ca30c66] bg-[#0ca30c14] text-[#a6e6a6]'
    : tone === 'warn' ? 'border-[#fab21955] bg-[#fab21912] text-[#fcd889]'
    : 'border-[#3987e555] bg-[#3987e512] text-[#b7d3f6]'
  return <div className={`rounded-lg border px-3 py-2 text-sm ${cls}`}>{children}</div>
}

export function StatTile({ label, value, sub }: { label: string; value: ReactNode; sub?: ReactNode }) {
  return (
    <div className="card px-4 py-3">
      <div className="label">{label}</div>
      <div className="mt-1 text-2xl font-semibold tabular-nums">{value}</div>
      {sub && <div className="mt-0.5 text-xs text-mute">{sub}</div>}
    </div>
  )
}

export function Section({ title, right, children, className = '' }: { title: ReactNode; right?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={`card p-4 ${className}`}>
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold text-ink">{title}</h2>
        {right}
      </div>
      {children}
    </section>
  )
}

export function Modal({ open, onClose, title, children, wide }: { open: boolean; onClose: () => void; title: string; children: ReactNode; wide?: boolean }) {
  useEffect(() => {
    const k = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', k)
    return () => window.removeEventListener('keydown', k)
  }, [onClose])
  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={onClose}>
      <div className={`card max-h-[90vh] w-full overflow-auto p-5 ${wide ? 'max-w-5xl' : 'max-w-lg'}`} onClick={e => e.stopPropagation()}>
        <div className="mb-4 flex items-center justify-between">
          <h3 className="font-semibold">{title}</h3>
          <button className="text-mute hover:text-ink" onClick={onClose}>✕</button>
        </div>
        {children}
      </div>
    </div>
  )
}

/** Re-renders every `ms` (for live timers). */
export function useTick(ms = 1000, active = true) {
  const [, set] = useState(0)
  useEffect(() => {
    if (!active) return
    const t = setInterval(() => set(x => x + 1), ms)
    return () => clearInterval(t)
  }, [ms, active])
}
