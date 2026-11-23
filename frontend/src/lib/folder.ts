import JSZip from 'jszip'

/**
 * Folder access. Chrome/Edge: File System Access API, with the handle remembered per problem in IndexedDB
 * (pick once, re-read on every Run/Hint/Submit). Other browsers: a one-off <input webkitdirectory> pick.
 */

const SKIP = new Set(['bin', 'obj', '.git', '.idea', '.vs', 'node_modules', 'TestResults'])
const TEXT_EXT = /\.(cs|csproj|props|targets|md|txt|json|slnx|sln|ps1|xml|editorconfig)$/i

export interface PickedFile { path: string; file: File }
export interface FolderSource { name: string; files: () => Promise<PickedFile[]> }

export const supportsHandles = () => typeof (window as any).showDirectoryPicker === 'function'

// ---------- IndexedDB ----------
function idb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open('dsa-tracker', 1)
    req.onupgradeneeded = () => req.result.createObjectStore('folders')
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}
async function idbGet<T>(key: string): Promise<T | undefined> {
  try {
    const db = await idb()
    return await new Promise((resolve, reject) => {
      const r = db.transaction('folders').objectStore('folders').get(key)
      r.onsuccess = () => resolve(r.result as T)
      r.onerror = () => reject(r.error)
    })
  } catch { return undefined }
}
async function idbSet(key: string, value: unknown) {
  try {
    const db = await idb()
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction('folders', 'readwrite')
      tx.objectStore('folders').put(value, key)
      tx.oncomplete = () => resolve()
      tx.onerror = () => reject(tx.error)
    })
  } catch { /* storage unavailable: the user just picks again next time */ }
}
async function idbDelete(key: string) {
  try {
    const db = await idb()
    db.transaction('folders', 'readwrite').objectStore('folders').delete(key)
  } catch { /* ignore */ }
}

// ---------- walking ----------
async function walkHandle(dir: any, prefix = ''): Promise<PickedFile[]> {
  const out: PickedFile[] = []
  for await (const [name, entry] of dir.entries()) {
    if (entry.kind === 'directory') {
      if (SKIP.has(name)) continue
      out.push(...(await walkHandle(entry, `${prefix}${name}/`)))
    } else if (TEXT_EXT.test(name) || name === '.tracker.json') {
      out.push({ path: prefix + name, file: await entry.getFile() })
    }
  }
  return out
}

function fromHandle(handle: any): FolderSource {
  return { name: handle.name, files: () => walkHandle(handle) }
}

async function ensurePermission(handle: any): Promise<boolean> {
  const opts = { mode: 'read' }
  if ((await handle.queryPermission?.(opts)) === 'granted') return true
  return (await handle.requestPermission?.(opts)) === 'granted'
}

/** Fallback picker for browsers without showDirectoryPicker. */
function pickWithInput(): Promise<FolderSource | null> {
  return new Promise(resolve => {
    const input = document.createElement('input')
    input.type = 'file'
    ;(input as any).webkitdirectory = true
    input.multiple = true
    input.onchange = () => {
      const list = Array.from(input.files ?? [])
      if (list.length === 0) return resolve(null)
      const root = list[0].webkitRelativePath.split('/')[0]
      const files = list
        .map(f => ({ path: f.webkitRelativePath.split('/').slice(1).join('/'), file: f }))
        .filter(f => !f.path.split('/').some(seg => SKIP.has(seg)) && (TEXT_EXT.test(f.path) || f.path.endsWith('.tracker.json')))
      resolve({ name: root, files: async () => files })
    }
    input.oncancel = () => resolve(null)
    input.click()
  })
}

/** Asks the user for a folder. With `rememberKey`, the choice is remembered (Chrome/Edge). */
export async function pickFolder(rememberKey?: string): Promise<FolderSource | null> {
  if (supportsHandles()) {
    try {
      const handle = await (window as any).showDirectoryPicker({ id: 'dsa-tracker', mode: 'read' })
      if (rememberKey) await idbSet(rememberKey, handle)
      return fromHandle(handle)
    } catch (e: any) {
      if (e?.name === 'AbortError') return null
      throw e
    }
  }
  return pickWithInput()
}

/** The remembered folder for a key, if the browser still lets us read it (asks once per session if needed). */
export async function rememberedFolder(key: string, askPermission = true): Promise<FolderSource | null> {
  if (!supportsHandles()) return null
  const handle = await idbGet<any>(key)
  if (!handle) return null
  if (askPermission ? !(await ensurePermission(handle)) : (await handle.queryPermission?.({ mode: 'read' })) !== 'granted') return null
  return fromHandle(handle)
}

export async function rememberedFolderName(key: string): Promise<string | null> {
  if (!supportsHandles()) return null
  const handle = await idbGet<any>(key)
  return handle?.name ?? null
}

export const forgetFolder = (key: string) => idbDelete(key)

// ---------- zip / read ----------
export async function zipFolder(src: FolderSource, onProgress?: (msg: string) => void): Promise<{ blob: Blob; count: number }> {
  const files = await src.files()
  const zip = new JSZip()
  for (const f of files) zip.file(f.path, f.file)
  onProgress?.(`Zipping ${files.length} files…`)
  const blob = await zip.generateAsync({ type: 'blob', compression: 'DEFLATE', compressionOptions: { level: 6 } },
    meta => onProgress?.(`Zipping… ${meta.percent.toFixed(0)}%`))
  return { blob, count: files.length }
}

/** Reads the problem's Solution.cs from the folder (wherever the exercise was unzipped inside it). */
export async function readSolution(src: FolderSource, relPath: string): Promise<string | null> {
  const files = await src.files()
  const hit = files.find(f => f.path === `${relPath}/Solution.cs` || f.path.endsWith(`/${relPath}/Solution.cs`))
  return hit ? await hit.file.text() : null
}

export const folderKey = (problemId: number) => `problem-${problemId}`
