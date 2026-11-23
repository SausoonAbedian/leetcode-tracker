export class ApiError extends Error {
  constructor(message: string, public status: number, public details?: any) {
    super(message)
  }
}

async function handle<T>(res: Response): Promise<T> {
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const body = text ? JSON.parse(text) : null
  if (!res.ok) throw new ApiError(body?.error ?? `Request failed (${res.status})`, res.status, body?.details)
  return body as T
}

export const api = {
  get: <T>(url: string) => fetch(url).then(r => handle<T>(r)),
  post: <T>(url: string, body?: unknown) =>
    fetch(url, {
      method: 'POST',
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    }).then(r => handle<T>(r)),
  put: <T>(url: string, body: unknown) =>
    fetch(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }).then(r => handle<T>(r)),
  patch: <T>(url: string, body: unknown) =>
    fetch(url, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }).then(r => handle<T>(r)),
  upload: <T>(url: string, zip: Blob, onProgress?: (pct: number) => void) =>
    new Promise<T>((resolve, reject) => {
      const xhr = new XMLHttpRequest()
      xhr.open('POST', url)
      xhr.upload.onprogress = e => e.lengthComputable && onProgress?.(Math.round((e.loaded / e.total) * 100))
      xhr.onload = () => {
        const body = xhr.responseText ? JSON.parse(xhr.responseText) : null
        if (xhr.status >= 200 && xhr.status < 300) resolve(body)
        else reject(new ApiError(body?.error ?? `Upload failed (${xhr.status})`, xhr.status, body?.details))
      }
      xhr.onerror = () => reject(new ApiError('Network error', 0))
      const form = new FormData()
      form.append('file', zip, 'upload.zip')
      xhr.send(form)
    }),
  /** POSTs (or GETs) and saves the returned file through the browser. */
  download: async (url: string, method: 'GET' | 'POST' = 'POST') => {
    const res = await fetch(url, { method })
    if (!res.ok) await handle(res)
    const blob = await res.blob()
    const cd = res.headers.get('Content-Disposition') ?? ''
    const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)?.[1] ?? 'problem.zip'
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = decodeURIComponent(name)
    document.body.appendChild(a)
    a.click()
    a.remove()
    setTimeout(() => URL.revokeObjectURL(a.href), 10000)
  },
}

// ---------- types ----------
export type Difficulty = 'Easy' | 'Medium' | 'Hard'

export interface Attempt {
  id: number; problemId: number; problemTitle?: string; difficulty?: Difficulty
  status: 'Downloaded' | 'Running' | 'Paused' | 'Passed' | 'Failed'
  failReason?: 'TestsFailed' | 'GaveUp' | null
  createdAt: string; startedAt?: string | null; pausedAt?: string | null; endedAt?: string | null
  activeSeconds: number; limitMinutes: number; overTime: boolean; hinted: boolean; hintsUsed: number
  testsPassed?: number | null; testsTotal?: number | null; notes?: string | null; hasCode: boolean
}

export interface ProblemRow {
  id: number; title: string; difficulty: Difficulty; topicNo: string; topic: string; lcNumber: number; solveUrl: string
  status: 'New' | 'InProgress' | 'Solved' | 'Failed' | 'SolvedBeforeFailedLast'
  attempts: number; passes: number; bestSeconds?: number | null; lastSeconds?: number | null; lastTried?: string | null
  lastStatus?: string | null; lastHinted: boolean; lastOverTime: boolean; limitMinutes: number
  reviewStage: number; nextReviewAt?: string | null
}

export interface AiEntry { id: number; kind: string; provider: string; model: string; response: string; at: string; attemptId?: number | null }

export interface ProblemDetail {
  id: number; title: string; difficulty: Difficulty; topicNo: string; topic: string; lcNumber: number
  lcUrl: string; ncUrl: string; solveUrl: string; source: string; relPath: string; projName: string; folder: string
  readme?: string; stub?: string; limitMinutes: number; attempts: Attempt[]; ai: AiEntry[]
  reviewStage: number; nextReviewAt?: string | null; intervals: number[]
}

export interface TestCase { name: string; outcome: string; message?: string | null; durationMs: number }
export interface TestRun {
  passed: boolean; passedCount: number; failedCount: number; total: number; buildFailed: boolean
  tests: TestCase[]; compileErrors: string[]; output: string; seconds: number; warnings: string[]
}

export interface QueueItem {
  problemId: number; title: string; difficulty: Difficulty; topicNo: string; topic: string; rule: string
  attempts: number; lastEndedAt?: string | null; lastStatus?: string | null; lastSeconds?: number | null
  lastHinted: boolean; lastOverTime: boolean; isOpen: boolean; reason: string
  stage: number; dueAt?: string | null
}
export interface QueueView {
  groups: { rule: string; label: string; priority: number; items: QueueItem[] }[]
  cooling: { problemId: number; title: string; difficulty: Difficulty; topicNo: string; topic: string; failedAt: string; readyAt: string }[]
  excluded: QueueItem[]; next?: QueueItem | null; total: number
  daily?: DailyPlan | null
}
export interface DailyPlan {
  categories: { key: string; label: string; quota: number; done: number; available: number }[]
  items: QueueItem[]; target: number; done: number
}
/** Query param so the server knows where the viewer's "today" starts. */
export const tzParam = () => `tz=${new Date().getTimezoneOffset()}`
export const CATEGORY_OF: Record<string, string> = {
  New: 'New', FailedReady: 'Failed / flagged', Hinted: 'Failed / flagged', OverTime: 'Failed / flagged', ReviewDue: 'Review',
}

export interface Topic { topicNo: string; topic: string; count: number }

export interface Settings {
  rules: { rule: string; enabled: boolean }[]
  failCooldownHours: number
  timeLimits: Record<string, number>
  reviewIntervalsDays: number[]
  dailyQuota: Record<string, number>
  ai: { provider: string; anthropicModel: string; deepSeekModel: string; claudeCodeModel: string }
}

export interface ProviderStatus { id: string; name: string; configured: boolean; hint: string; selected: boolean }

export interface AiAnswer { interactionId: number; kind: string; text: string; provider: string; model: string; attemptId?: number | null; hintsUsed?: number | null; at: string }

export interface TopicRow {
  topicNo: string; topic: string; problems: number; solved: number; attempts: number; passRate: number; hintRate: number
  overTimeRate: number; avgMinutes?: number | null; score?: number | null; strength: string
}
export interface Stats {
  scope: string
  unique: { total: number; solved: number; attemptedNotSolved: number; untouched: number
    byDifficulty: { difficulty: Difficulty; total: number; solved: number; attemptedNotSolved: number; untouched: number }[] }
  attempts: { total: number; passed: number; failed: number; gaveUp: number; hinted: number; overTime: number
    passRate: number; hintRate: number; overTimeRate: number
    times: { difficulty: Difficulty; passed: number; avgMinutes?: number | null; medianMinutes?: number | null; limitMinutes: number }[]
    heatmap: { day: string; attempts: number; passed: number }[]; currentStreak: number; longestStreak: number }
  topics: TopicRow[]; strong: TopicRow[]; weak: TopicRow[]
}

export interface ImportResult {
  importId: number; problemCount: number; added: number; updated: number; unchanged: number; sharedFiles: number
  topics: { topicNo: string; topic: string; easy: number; medium: number; hard: number; total: number }[]
  warnings: string[]
}
