import { CATEGORY_OF, DailyPlan } from '../lib/api'
import { QueueRow } from './QueueList'

export function DailyProgress({ plan }: { plan: DailyPlan }) {
  return (
    <div className="mb-3 flex flex-wrap items-center gap-2 text-xs">
      <span className="font-medium text-ink">{plan.done}/{plan.target} done today</span>
      {plan.categories.filter(c => c.quota > 0 || c.done > 0).map(c => (
        <span key={c.key} className="rounded-full border border-line px-2 py-0.5 text-ink2" title={`${c.available} available`}>
          {c.label} {Math.min(c.done, c.quota)}/{c.quota}
        </span>
      ))}
    </div>
  )
}

export function DailyList({ plan }: { plan: DailyPlan }) {
  if (plan.items.length === 0)
    return <p className="text-sm text-mute">{plan.done >= plan.target ? 'Done for today 🎉' : 'Nothing available for today.'}</p>
  return (
    <div className="-mx-3">
      {plan.items.map((it, i) => (
        <div key={it.problemId} className="flex items-center">
          <span className="w-28 shrink-0 pl-3 text-xs text-mute">{CATEGORY_OF[it.rule] ?? it.rule}</span>
          <div className="min-w-0 flex-1"><QueueRow item={it} rank={i + 1} /></div>
        </div>
      ))}
    </div>
  )
}
