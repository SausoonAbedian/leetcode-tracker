import { lazy, Suspense } from 'react'
import { Link, NavLink, Route, Routes } from 'react-router-dom'
import { liveSeconds, useActiveAttempt } from './lib/activeAttempt'
import { fmtDuration } from './lib/format'
import { Spinner, useTick } from './components/ui'
import Dashboard from './pages/Dashboard'
import QueuePage from './pages/QueuePage'
import ProblemsPage from './pages/ProblemsPage'
import StatsPage from './pages/StatsPage'
import SettingsPage from './pages/SettingsPage'
import ImportPage from './pages/ImportPage'

const ProblemPage = lazy(() => import('./pages/ProblemPage'))   // Monaco lives here

const NAV = [
  { to: '/', label: 'Dashboard', icon: '◧' },
  { to: '/queue', label: 'Queue', icon: '☰' },
  { to: '/problems', label: 'Problems', icon: '▤' },
  { to: '/stats', label: 'Statistics', icon: '▦' },
  { to: '/settings', label: 'Settings', icon: '⚙' },
  { to: '/import', label: 'Import', icon: '⇪' },
]

function ActivePill() {
  const { active, fetchedAt } = useActiveAttempt()
  useTick(1000, active?.status === 'Running')
  if (!active) return null
  const secs = liveSeconds(active, fetchedAt)
  const over = active.startedAt && secs > active.limitMinutes * 60
  return (
    <Link to={`/problems/${active.problemId}`} className="mx-3 mb-3 block rounded-lg border border-line bg-s2 px-3 py-2 hover:bg-s3">
      <div className="label">Open attempt</div>
      <div className="truncate text-sm font-medium">NC{String(active.problemId).padStart(3, '0')} {active.problemTitle}</div>
      <div className="font-mono text-sm tabular-nums" style={{ color: over ? 'var(--warning)' : 'var(--text-secondary)' }}>
        {active.status === 'Downloaded' ? 'not started' : `${fmtDuration(secs)}${active.status === 'Paused' ? ' · paused' : ''}`}
      </div>
    </Link>
  )
}

export default function App() {
  return (
    <div className="flex h-full">
      <aside className="flex w-56 shrink-0 flex-col border-r border-line bg-s1">
        <div className="flex items-center gap-2 px-4 py-4">
          <span className="grid size-7 place-items-center rounded-lg bg-accent text-sm font-bold text-white">✓</span>
          <span className="font-semibold">DSA Tracker</span>
        </div>
        <nav className="flex-1 space-y-0.5 px-2">
          {NAV.map(n => (
            <NavLink key={n.to} to={n.to} end={n.to === '/'}
              className={({ isActive }) => `flex items-center gap-3 rounded-lg px-3 py-2 text-sm ${isActive ? 'bg-s3 text-ink' : 'text-ink2 hover:bg-s2'}`}>
              <span className="w-4 text-center text-mute">{n.icon}</span>{n.label}
            </NavLink>
          ))}
        </nav>
        <ActivePill />
      </aside>
      <main className="min-w-0 flex-1 overflow-y-auto">
        <div className="mx-auto max-w-7xl p-6">
          <Suspense fallback={<Spinner />}>
            <Routes>
              <Route path="/" element={<Dashboard />} />
              <Route path="/queue" element={<QueuePage />} />
              <Route path="/problems" element={<ProblemsPage />} />
              <Route path="/problems/:id" element={<ProblemPage />} />
              <Route path="/stats" element={<StatsPage />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/import" element={<ImportPage />} />
            </Routes>
          </Suspense>
        </div>
      </main>
    </div>
  )
}
