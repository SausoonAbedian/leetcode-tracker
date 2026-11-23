import { createContext, ReactNode, useCallback, useContext, useEffect, useState } from 'react'
import { api, Attempt } from './api'

interface Ctx { active: Attempt | null; fetchedAt: number; refresh: () => Promise<void> }
const ActiveCtx = createContext<Ctx>({ active: null, fetchedAt: 0, refresh: async () => {} })

export function ActiveAttemptProvider({ children }: { children: ReactNode }) {
  const [active, setActive] = useState<Attempt | null>(null)
  const [fetchedAt, setFetchedAt] = useState(Date.now())
  const refresh = useCallback(async () => {
    try {
      setActive(await api.get<Attempt | null>('/api/attempts/active'))
      setFetchedAt(Date.now())
    } catch { /* server not reachable yet */ }
  }, [])
  useEffect(() => {
    refresh()
    const t = setInterval(refresh, 30000)
    return () => clearInterval(t)
  }, [refresh])
  return <ActiveCtx.Provider value={{ active, fetchedAt, refresh }}>{children}</ActiveCtx.Provider>
}

export const useActiveAttempt = () => useContext(ActiveCtx)

/** Live active seconds for an attempt DTO fetched at `fetchedAt`. */
export function liveSeconds(a: Attempt, fetchedAt: number): number {
  return a.status === 'Running' ? a.activeSeconds + (Date.now() - fetchedAt) / 1000 : a.activeSeconds
}
