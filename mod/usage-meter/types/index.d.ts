export type Limit = { kind: string; percentUsed: number; resetsAt?: string }

export type Snap = {
  limits: Limit[]
  costUsd?: number
  contextPercent?: number
  contextTokens?: number
  contextWindow: number
  /** Ngưỡng token tự compact; undefined khi tắt hoặc chưa đo */
  autoCompactAt?: number
  isAutoCompact?: boolean
  /** Cửa sổ app dùng để tính % context (rawMaxTokens), có thể nhỏ hơn contextWindow */
  compactWindow?: number
  startedAt: number
  at: number
}

export type Tok = { input: number; output: number; cacheRead: number; cacheWrite: number; turns: number }

export type TokMap = Record<string, Tok>

/** model -> effort gần nhất; `main` là model của luồng chính */
export type EffortMap = { main?: string; byModel: Record<string, string> }

declare module 'claude-code' {
  interface PluginState {
    'usage-meter': {
      snap: Shaped<Snap | null>
      tokens: Shaped<TokMap>
      lastCost: Shaped<number>
      baseCost: Shaped<number>
      history: Shaped<{ today: number; week: number }>
      effort: Shaped<EffortMap>
      spark: Shaped<Array<[number, number]>>
    }
  }
}
