export function fmtDuration(seconds?: number | null): string {
  if (seconds == null) return '—'
  const s = Math.max(0, Math.round(seconds))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const sec = s % 60
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}` : `${m}:${String(sec).padStart(2, '0')}`
}

export function fmtMinutes(seconds?: number | null): string {
  if (seconds == null) return '—'
  const m = seconds / 60
  return m < 10 ? `${m.toFixed(1)} min` : `${Math.round(m)} min`
}

export function fmtDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}

export function fmtAgo(iso?: string | null): string {
  if (!iso) return 'never'
  const diff = (Date.now() - new Date(iso).getTime()) / 1000
  if (diff < 60) return 'just now'
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`
  const d = Math.floor(diff / 86400)
  return d === 1 ? 'yesterday' : `${d}d ago`
}

export function fmtIn(iso: string): string {
  const diff = (new Date(iso).getTime() - Date.now()) / 1000
  if (diff <= 0) return 'now'
  if (diff < 3600) return `in ${Math.ceil(diff / 60)}m`
  return `in ${Math.floor(diff / 3600)}h ${Math.round((diff % 3600) / 60)}m`
}

/** "due in 3d" / "due 2d ago" / "due now" */
export function fmtDue(iso?: string | null): string {
  if (!iso) return '—'
  const diff = (new Date(iso).getTime() - Date.now()) / 1000
  const span = (s: number) => (s >= 86400 ? `${Math.round(s / 86400)}d` : s >= 3600 ? `${Math.round(s / 3600)}h` : `${Math.max(1, Math.round(s / 60))}m`)
  if (Math.abs(diff) < 60) return 'due now'
  return diff > 0 ? `due in ${span(diff)}` : `due ${span(-diff)} ago`
}

export const pct =(x: number) => (x > 0 && x < 0.01 ? `${(x * 100).toFixed(1)}%` : `${Math.round(x * 100)}%`)

/** Value for <input type="datetime-local"> in local time. */
export function toLocalInput(d: Date): string {
  const off = d.getTimezoneOffset() * 60000
  return new Date(d.getTime() - off).toISOString().slice(0, 16)
}
