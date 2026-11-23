import { useState } from 'react'
import type { TestRun } from '../lib/api'
import { Badge, Notice } from './ui'

export function TestResults({ run, warnings, saved }: { run: TestRun; warnings?: string[]; saved?: boolean }) {
  const [showLog, setShowLog] = useState(false)
  const all = [...(warnings ?? []), ...run.warnings]
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        {run.buildFailed ? <Badge tone="bad">✕ Build failed</Badge>
          : run.passed ? <Badge tone="good">✓ All {run.total} tests passed</Badge>
          : <Badge tone="bad">✕ {run.failedCount} of {run.total} tests failed</Badge>}
        <span className="text-xs text-mute">{run.seconds.toFixed(1)}s · {saved ? 'saved as this attempt\'s result' : 'check only — not saved'}</span>
        <button className="ml-auto text-xs text-mute hover:text-ink" onClick={() => setShowLog(s => !s)}>{showLog ? 'Hide' : 'Show'} build log</button>
      </div>
      {all.map((w, i) => <Notice key={i} tone="warn">{w}</Notice>)}
      {run.compileErrors.length > 0 && (
        <pre className="max-h-60 overflow-auto rounded-lg border border-line bg-s0 p-3 font-mono text-xs text-[#f3b0b0]">{run.compileErrors.join('\n')}</pre>
      )}
      {run.tests.length > 0 && (
        <ul className="divide-y divide-[#2a2a27] overflow-hidden rounded-lg border border-line">
          {run.tests.map(t => (
            <li key={t.name} className="px-3 py-1.5 text-sm">
              <div className="flex items-center gap-2">
                <span aria-label={t.outcome} style={{ color: t.outcome === 'Passed' ? 'var(--good)' : 'var(--critical)' }}>{t.outcome === 'Passed' ? '✓' : '✕'}</span>
                <span className="font-mono text-xs">{t.name}</span>
                <span className="ml-auto text-xs text-mute">{t.outcome}</span>
              </div>
              {t.message && <pre className="mt-1 whitespace-pre-wrap font-mono text-xs text-mute">{t.message}</pre>}
            </li>
          ))}
        </ul>
      )}
      {showLog && <pre className="max-h-72 overflow-auto rounded-lg border border-line bg-s0 p-3 font-mono text-[11px] text-mute">{run.output}</pre>}
    </div>
  )
}
