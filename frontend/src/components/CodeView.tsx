import Editor, { DiffEditor, loader } from '@monaco-editor/react'
import * as monaco from 'monaco-editor'
import EditorWorker from 'monaco-editor/esm/vs/editor/editor.worker?worker'

// Bundle Monaco locally (no CDN at runtime).
;(self as any).MonacoEnvironment = { getWorker: () => new EditorWorker() }
loader.config({ monaco })

const options = {
  readOnly: true, minimap: { enabled: false }, fontSize: 13, scrollBeyondLastLine: false,
  fontFamily: 'JetBrains Mono, Cascadia Code, Consolas, monospace', renderLineHighlight: 'none' as const,
  lineNumbersMinChars: 3, padding: { top: 8, bottom: 8 }, automaticLayout: true,
}

export function CodeView({ code, height = 380 }: { code: string; height?: number }) {
  const lines = code.split('\n').length
  const h = Math.min(height, Math.max(120, lines * 19 + 24))
  return (
    <div className="overflow-hidden rounded-lg border border-line">
      <Editor height={h} language="csharp" theme="vs-dark" value={code} options={options} />
    </div>
  )
}

export function CodeDiff({ left, right, height = 460 }: { left: string; right: string; height?: number }) {
  return (
    <div className="overflow-hidden rounded-lg border border-line">
      <DiffEditor height={height} language="csharp" theme="vs-dark" original={left} modified={right}
        options={{ ...options, renderSideBySide: true }} />
    </div>
  )
}
