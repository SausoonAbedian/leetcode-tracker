import { Link } from 'react-router-dom'
import type { QueueItem } from '../lib/api'
import { fmtAgo, fmtDue, fmtMinutes } from '../lib/format'
import { Badge, DifficultyBadge, Flags, StageBadge } from './ui'

export function QueueRow({ item, rank }: { item: QueueItem; rank?: number }) {
  const upcoming = item.rule === 'ReviewUpcoming'
  const isReview = upcoming || item.rule === 'ReviewDue'
  return (
    <Link to={`/problems/${item.problemId}`} className={`flex items-center gap-3 rounded-lg px-3 py-2 hover:bg-s2 ${upcoming ? 'opacity-60' : ''}`}>
      {rank != null && <span className="w-6 text-right font-mono text-xs text-mute">{rank}</span>}
      <span className="w-12 font-mono text-xs text-mute">NC{String(item.problemId).padStart(3, '0')}</span>
      <span className="min-w-[8rem] flex-1 truncate text-sm" title={item.reason}>{item.title}</span>
      {item.isOpen && <Badge tone="info">● open</Badge>}
      <StageBadge stage={item.stage} />
      {isReview && <span className="text-xs whitespace-nowrap" style={{ color: upcoming ? 'var(--text-muted)' : 'var(--text-secondary)' }}>{fmtDue(item.dueAt)}</span>}
      <Flags hinted={item.lastHinted} overTime={item.lastOverTime} />
      <span className="hidden w-44 truncate text-xs text-mute xl:block">{isReview ? '' : item.reason}</span>
      <span className="hidden w-28 truncate text-xs text-mute 2xl:block">{item.topic}</span>
      <span className="w-20"><DifficultyBadge d={item.difficulty} /></span>
      <span className="w-24 text-right text-xs text-mute">{item.attempts ? `${item.attempts}× · ${fmtAgo(item.lastEndedAt)}` : 'never'}</span>
      <span className="hidden w-16 text-right text-xs tabular-nums text-mute sm:block">{fmtMinutes(item.lastSeconds)}</span>
    </Link>
  )
}
