import { atom, read, update } from 'claude-code'
import type { Register, EngineInterface, SessionRateLimit, ModelUsage, Elements } from 'claude-code'

import type { EffortMap, Limit, Snap, Tok, TokMap } from '../types'

const PANE = 'usage-meter'
const TITLE = 'Claude Usage'

const snapA = atom({ plugin: 'usage-meter', key: 'snap' } as const, null as Snap | null, { shape: 'snap-2' })
const tokA = atom({ plugin: 'usage-meter', key: 'tokens' } as const, {} as TokMap, { shape: 'tok-1' })
const lastCostA = atom({ plugin: 'usage-meter', key: 'lastCost' } as const, 0, { shape: 'lc-1' })
const baseA = atom({ plugin: 'usage-meter', key: 'baseCost' } as const, -1, { shape: 'base-1' })
const histA = atom({ plugin: 'usage-meter', key: 'history' } as const, { today: 0, week: 0 }, { shape: 'hist-1' })
const effA = atom({ plugin: 'usage-meter', key: 'effort' } as const, { byModel: {} } as EffortMap, { shape: 'eff-1' })
const sparkA = atom({ plugin: 'usage-meter', key: 'spark' } as const, [] as Array<[number, number]>, { shape: 'spark-1' })

// ---------- Bảng giá (USD / 1 triệu token) ----------
// Nguồn: skill claude-api, bảng Current Models (cached 2026-11-03).
// [mẫu tên model, input, output, cache read]. Ghi cache: Claude Code dùng TTL 1 giờ = 2x input.
const PRICE: Array<[RegExp, number, number, number]> = [
  [/fable|mythos/i, 8, 40, 0.2],
  [/opus/i, 4, 20, 0.2],
  [/sonnet/i, 2, 10, 0.1],
  [/haiku/i, 0.1, 0.5, 0.01],
]
const CACHE_WRITE_X = 2
function price(model: string): [number, number, number] {
  for (const [re, i, o, r] of PRICE) if (re.test(model)) return [i, o, r]
  return [4, 20, 0.2]
}
function estUsd(model: string, t: Tok) {
  const [i, o, r] = price(model)
  const m = 1e6
  return {
    input: (t.input * i) / m,
    output: (t.output * o) / m,
    cacheWrite: (t.cacheWrite * i * CACHE_WRITE_X) / m,
    cacheRead: (t.cacheRead * r) / m,
  }
}

// ---------- Định dạng ----------
const fmtUsd = (v: number) => (v >= 100 ? `$${v.toFixed(0)}` : v >= 1 ? `$${v.toFixed(2)}` : `$${v.toFixed(3)}`)
function fmtTok(n: number) {
  if (n >= 1e9) return `${(n / 1e9).toFixed(1)}B`
  if (n >= 1e6) return `${(n / 1e6).toFixed(1)}M`
  if (n >= 1e3) return `${Math.round(n / 1e3)}k`
  return `${n}`
}
function fmtDur(ms: number) {
  if (ms <= 0) return '0m'
  const m = Math.round(ms / 60000)
  const d = Math.floor(m / 1440)
  const h = Math.floor((m % 1440) / 60)
  const mm = m % 60
  if (d > 0) return `${d}d ${h}h`
  if (h > 0) return `${h}h ${mm}m`
  return `${mm}m`
}
function fmtAgo(ms: number) {
  if (ms < 60000) return `${Math.max(1, Math.round(ms / 1000))}s`
  return fmtDur(ms)
}
function dayKey(ms: number) {
  const d = new Date(ms)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
function prettyModel(id: string) {
  const m = id.match(/(fable|mythos|opus|sonnet|haiku)[-_]?(\d+(?:[-.]\d+)?)?/i)
  if (!m || !m[1]) return id.replace(/^claude-/, '')
  const fam = m[1].charAt(0).toUpperCase() + m[1].slice(1).toLowerCase()
  return m[2] ? `${fam} ${m[2].replace('-', '.')}` : fam
}
const esc = (s: string) =>
  s.replace(/[&<>"]/g, c => (c === '&' ? '&amp;' : c === '<' ? '&lt;' : c === '>' ? '&gt;' : '&quot;'))

function labelOf(kind: string) {
  if (kind === 'five_hour') return 'PHIÊN 5 GIỜ'
  if (kind === 'seven_day') return 'TUẦN'
  if (kind === 'spend_limit') return 'HẠN MỨC CHI'
  const m = kind.match(/(fable|opus|sonnet|haiku)/i)
  const w = m?.[1]
  if (w) return `TUẦN · ${w.toUpperCase()}`
  return kind.replace(/_/g, ' ').toUpperCase()
}
function windowMs(kind: string) {
  if (kind.startsWith('five_hour')) return 5 * 3600e3
  if (kind.startsWith('seven_day') || /fable|opus|sonnet|haiku/i.test(kind)) return 7 * 86400e3
  return 0
}
const ORDER = (k: string) => (k === 'five_hour' ? 0 : k === 'seven_day' ? 2 : k === 'spend_limit' ? 9 : 1)

type Pace = { expected?: number; resetIn?: number; ratePerUnit?: number; unit?: 'h' | 'ngày'; fullIn?: number }
function pace(l: Limit, now: number): Pace {
  if (!l.resetsAt) return {}
  const reset = Date.parse(l.resetsAt)
  if (!isFinite(reset)) return {}
  const resetIn = reset - now
  const len = windowMs(l.kind)
  if (!len) return { resetIn }
  const elapsed = Math.min(len, Math.max(0, len - resetIn))
  const expected = (elapsed / len) * 100
  if (elapsed < len * 0.02 || l.percentUsed <= 0) return { expected, resetIn }
  const perMs = l.percentUsed / elapsed
  const unit: 'h' | 'ngày' = len <= 6 * 3600e3 ? 'h' : 'ngày'
  const ratePerUnit = perMs * (unit === 'h' ? 3600e3 : 86400e3)
  const fullIn = l.percentUsed < 100 ? (100 - l.percentUsed) / perMs : 0
  return { expected, resetIn, ratePerUnit, unit, fullIn }
}

// ---------- Sổ chi phí qua nhiều phiên ($.store) ----------
// Mỗi lần chạy của phiên (sid + startedAt; chi phí app báo là cộng dồn từ startedAt) ghi thẳng phần chi
// trong ngày = chi phí hiện tại − mốc đầu ngày. Ghi đè, không cộng dồn delta, nên nạp lại hay cài lại
// mod không làm hụt hay trùng tiền.
async function ledger($: EngineInterface, costUsd: number, startedAt: number) {
  const now = await $.clock.now()
  const sid = await $.session.id()
  const day = dayKey(now)
  const run = `${sid}:${Math.round(startedAt)}`
  const bKey = `b:${day}:${run}`
  const stored = await $.store.get(bKey)
  // Lần chạy bắt đầu hôm nay thì mốc là 0; bắt đầu từ hôm trước thì mốc là chi phí lúc thấy lần đầu trong ngày
  let base = stored === undefined ? (dayKey(startedAt) === day ? 0 : costUsd) : Number(stored) || 0
  if (costUsd < base) base = 0
  if (stored === undefined || Number(stored) !== base) await $.store.set(bKey, base)
  await $.store.set(`d:${day}:${run}`, Math.max(0, costUsd - base))

  const last = await read($, lastCostA)
  if (costUsd !== last) {
    await update($, lastCostA, () => costUsd)
    await update($, sparkA, s => [...s, [now, costUsd] as [number, number]].slice(-120))
  }
  await refreshHistory($, now)
}
async function refreshHistory($: EngineInterface, now: number) {
  const keys = await $.store.keys()
  const days = new Set<string>()
  for (let i = 0; i < 7; i++) days.add(dayKey(now - i * 86400e3))
  const today = dayKey(now)
  const cutoff = dayKey(now - 35 * 86400e3)
  let t = 0
  let w = 0
  // Khoá kiểu cũ d:<ngày>:<sid> (cộng dồn delta, có thể hụt) chỉ tính khi ngày đó sid chưa có khoá kiểu mới
  const fresh = new Set(keys.filter(k => k.startsWith('d:') && k.split(':').length >= 4).map(k => k.split(':').slice(0, 3).join(':')))
  for (const k of keys) {
    if (!k.startsWith('d:') && !k.startsWith('b:')) continue
    const day = k.slice(2, 12)
    if (day < cutoff) {
      await $.store.delete(k)
      continue
    }
    if (!k.startsWith('d:') || !days.has(day)) continue
    if (k.split(':').length === 3 && fresh.has(k)) continue
    const v = Number((await $.store.get(k)) ?? 0) || 0
    w += v
    if (day === today) t += v
  }
  await update($, histA, () => ({ today: t, week: w }))
}

type Compact = { autoCompactAt?: number; isAutoCompact?: boolean; compactWindow?: number; ok: boolean }
async function compactInfo($: EngineInterface): Promise<Compact> {
  try {
    const u = await $.session.usage({ breakdown: 'summary' })
    const b = u.context.breakdown
    if (!b) return { ok: false }
    return { autoCompactAt: b.autoCompactThreshold, isAutoCompact: b.isAutoCompactEnabled, compactWindow: b.rawMaxTokens, ok: true }
  } catch {
    return { ok: false }
  }
}

async function take(
  $: EngineInterface,
  u: {
    context: { tokens?: number; window: number; percent?: number }
    rateLimits: SessionRateLimit[]
    cost?: { usd: number }
    startedAt?: number
  },
  withCompact: boolean,
) {
  const now = await $.clock.now()
  const prev = await read($, snapA)
  const ac: Compact = withCompact || prev?.isAutoCompact === undefined ? await compactInfo($) : { ok: false }
  const snap: Snap = {
    limits: [...u.rateLimits]
      .map(r => ({ kind: r.kind, percentUsed: r.percentUsed, resetsAt: r.resetsAt }))
      .sort((a, b) => ORDER(a.kind) - ORDER(b.kind)),
    costUsd: u.cost?.usd,
    contextPercent: u.context.percent,
    contextTokens: u.context.tokens,
    contextWindow: u.context.window,
    autoCompactAt: ac.ok ? ac.autoCompactAt : prev?.autoCompactAt,
    isAutoCompact: ac.ok ? ac.isAutoCompact : prev?.isAutoCompact,
    compactWindow: ac.ok ? ac.compactWindow : prev?.compactWindow,
    startedAt: u.startedAt ?? prev?.startedAt ?? now,
    at: now,
  }
  if (snap.limits.length === 0 && prev?.limits.length) snap.limits = prev.limits
  // App tính % context trên cửa sổ compact (rawMaxTokens), không phải cửa sổ model
  if (snap.compactWindow && snap.contextTokens !== undefined) {
    snap.contextWindow = snap.compactWindow
    snap.contextPercent = Math.round((snap.contextTokens / snap.compactWindow) * 100)
  }
  await update($, snapA, () => snap)
  if (u.cost) {
    const usd = u.cost.usd
    await update($, baseA, b => (b < 0 || usd < b ? usd : b))
    await ledger($, usd, snap.startedAt)
  }
  status($, snap)
  await exportJson($, true)
}

function status($: EngineInterface, s: Snap) {
  const parts: string[] = []
  for (const l of s.limits) {
    const short =
      l.kind === 'five_hour' ? '5h' : l.kind === 'seven_day' ? '7d' : labelOf(l.kind).replace('TUẦN · ', '').toLowerCase()
    parts.push(`${short} ${Math.round(l.percentUsed)}%`)
  }
  if (s.costUsd !== undefined) parts.push(fmtUsd(s.costUsd))
  $.ui.status(parts.length ? parts.join(' · ') : undefined)
}

function addTok(map: TokMap, model: string, u: ModelUsage): TokMap {
  const cur = map[model] ?? { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, turns: 0 }
  return {
    ...map,
    [model]: {
      input: cur.input + u.input_tokens,
      output: cur.output + u.output_tokens,
      cacheRead: cur.cacheRead + u.cache_read_input_tokens,
      cacheWrite: cur.cacheWrite + u.cache_creation_input_tokens,
      turns: cur.turns + 1,
    },
  }
}

// ---------- Dữ liệu dùng chung cho mọi giao diện ----------
type Split = { input: number; output: number; cacheWrite: number; cacheRead: number }
type View = {
  now: number
  s: Snap | null
  hist: { today: number; week: number }
  total: number
  burn: number
  models: Array<{ id: string; name: string; effort?: string; usd: number; tokens: number; share: number }>
  split: Split
  splitUsd: Split
  cacheHit: number
  spark: Array<[number, number]>
  mainModel?: string
  mainEffort?: string
}
async function view($: EngineInterface): Promise<View> {
  const s = await read($, snapA)
  const toks = await read($, tokA)
  const hist = await read($, histA)
  const base = await read($, baseA)
  const eff = await read($, effA)
  const spark = await read($, sparkA)
  const now = await $.clock.now()
  const split: Split = { input: 0, output: 0, cacheWrite: 0, cacheRead: 0 }
  const est: Split = { input: 0, output: 0, cacheWrite: 0, cacheRead: 0 }
  const per: Array<{ id: string; usd: number; tokens: number }> = []
  for (const [m, t] of Object.entries(toks)) {
    split.input += t.input
    split.output += t.output
    split.cacheWrite += t.cacheWrite
    split.cacheRead += t.cacheRead
    const x = estUsd(m, t)
    est.input += x.input
    est.output += x.output
    est.cacheWrite += x.cacheWrite
    est.cacheRead += x.cacheRead
    per.push({
      id: m,
      usd: x.input + x.output + x.cacheWrite + x.cacheRead,
      tokens: t.input + t.output + t.cacheRead + t.cacheWrite,
    })
  }
  const estTotal = est.input + est.output + est.cacheWrite + est.cacheRead
  const cost = s?.costUsd
  // Token chỉ đếm từ lúc mod chạy, nên chỉ chia phần chi phí phát sinh sau mốc đó
  const since = cost !== undefined && base >= 0 ? Math.max(0, cost - base) : undefined
  const scale = since !== undefined && since > 0 && estTotal > 0 ? since / estTotal : 1
  const total = cost ?? estTotal
  const hours = s ? Math.max(1 / 60, (now - s.startedAt) / 3600e3) : 1
  const sumUsd = per.reduce((a, p) => a + p.usd, 0) || 1
  const models = per
    .sort((a, b) => b.usd - a.usd)
    .map(p => ({
      id: p.id,
      name: prettyModel(p.id),
      effort: eff.byModel[p.id],
      usd: p.usd * scale,
      tokens: p.tokens,
      share: p.usd / sumUsd,
    }))
  const inAll = split.input + split.cacheRead + split.cacheWrite
  return {
    now,
    s,
    hist,
    total,
    burn: total / hours,
    models,
    split,
    splitUsd: {
      input: est.input * scale,
      output: est.output * scale,
      cacheWrite: est.cacheWrite * scale,
      cacheRead: est.cacheRead * scale,
    },
    cacheHit: inAll > 0 ? (split.cacheRead / inAll) * 100 : 0,
    spark,
    mainModel: eff.main,
    mainEffort: eff.main ? eff.byModel[eff.main] : undefined,
  }
}

// ---------- Xuất JSON cho widget ngoài (claude-usage-tray) ----------
// File ~/.claude/usage-meter.json; mọi phiên Claude Code ghi chung, phiên ghi sau cùng thắng.
let lastExport = 0
async function exportJson($: EngineInterface, force = false) {
  try {
    const now = await $.clock.now()
    if (!force && now - lastExport < 5000) return
    lastExport = now
    const home = (await $.env.get('USERPROFILE')) ?? (await $.env.get('HOME'))
    if (!home) return
    const v = await view($)
    const s = v.s
    const out = {
      schema: 1,
      writtenAt: new Date(now).toISOString(),
      sessionId: await $.session.id(),
      measuredAt: s ? new Date(s.at).toISOString() : null,
      limits: (s?.limits ?? []).map(l => {
        const p = pace(l, now)
        return {
          kind: l.kind,
          label: labelOf(l.kind),
          percentUsed: l.percentUsed,
          resetsAt: l.resetsAt ?? null,
          expectedPercent: p.expected ?? null,
          ratePerUnit: p.ratePerUnit ?? null,
          unit: p.unit ?? null,
          fullInMs: p.fullIn ?? null,
        }
      }),
      context: s
        ? {
            tokens: s.contextTokens ?? null,
            window: s.contextWindow,
            percent: s.contextPercent ?? null,
            autoCompactAt: s.isAutoCompact ? (s.autoCompactAt ?? null) : null,
          }
        : null,
      cost: { session: v.total, perHour: v.burn, today: v.hist.today, week: v.hist.week },
      tokens: { ...v.split, cacheHitPercent: v.cacheHit, usd: v.splitUsd },
      models: v.models.map(m => ({ id: m.id, name: m.name, effort: m.effort ?? null, usd: m.usd, share: m.share })),
      mainModel: v.mainModel ?? null,
      mainEffort: v.mainEffort ?? null,
    }
    await $.fs.write(`${home.split('\\').join('/')}/.claude/usage-meter.json`, JSON.stringify(out, null, 1))
  } catch {
    /* không chặn pane nếu ghi file lỗi */
  }
}

// ---------- SVG (desktop) ----------
// Bảng màu Darcula (JetBrains)
const C = {
  bg: '#2b2b2b',
  track: '#3c3f41',
  tick: '#515658',
  text: '#a9b7c6',
  dim: '#808080',
  faint: '#606366',
  g1: '#4f6f3f',
  g2: '#6a8759',
  amber: '#cc7832',
  red: '#bc3f3c',
  blue: '#6897bb',
  violet: '#9876aa',
  yellow: '#ffc66d',
}
function heat(p: number) {
  return p >= 85 ? C.red : p >= 65 ? C.amber : C.yellow
}
const EFF_COLOR: Record<string, string> = {
  low: '#515658',
  medium: '#3d5a73',
  high: '#5c4a6b',
  xhigh: '#7a5a38',
  max: '#7a3634',
}
const SPLIT = [
  ['input', 'Input', '#6897bb'],
  ['output', 'Output', '#9876aa'],
  ['cacheWrite', 'Cache ghi', '#cc7832'],
  ['cacheRead', 'Cache đọc', '#6a8759'],
] as const

function bar(
  x: number,
  y: number,
  w: number,
  h: number,
  pct: number,
  gradId: string,
  opts: { marks?: number[]; pace?: number; flag?: number } = {},
) {
  const f = Math.max(0, Math.min(100, pct))
  const r = h / 2
  let s = `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="${C.track}"/>`
  if (f > 0)
    s += `<rect x="${x}" y="${y}" width="${Math.max(h, (f / 100) * w).toFixed(1)}" height="${h}" rx="${r}" fill="url(#${gradId})"/>`
  for (const m of opts.marks ?? [25, 50, 75]) {
    const mx = x + (m / 100) * w
    s += `<rect x="${(mx - 0.5).toFixed(1)}" y="${y + 2}" width="1" height="${Math.max(1, h - 4)}" fill="${C.tick}" opacity="0.8"/>`
  }
  if (opts.pace !== undefined) {
    const px = x + (Math.min(100, opts.pace) / 100) * w
    s += `<rect x="${(px - 1.5).toFixed(1)}" y="${y - 3}" width="3" height="${h + 6}" rx="1.5" fill="${C.dim}"/>`
  }
  if (opts.flag !== undefined) {
    const fx = x + (Math.min(100, opts.flag) / 100) * w
    s += `<line x1="${fx.toFixed(1)}" y1="${y - 5}" x2="${fx.toFixed(1)}" y2="${y + h + 5}" stroke="${C.amber}" stroke-width="2" stroke-dasharray="3 2"/>`
  }
  return s
}

function buildSvg(v: View, W: number) {
  const P = 18
  const iw = W - P * 2
  let y = P
  let body = ''
  const txt = (x: number, yy: number, t: string, size: number, color: string, extra = '') =>
    `<text x="${x}" y="${yy}" font-size="${size}" fill="${color}" ${extra}>${esc(t)}</text>`
  const rule = () => {
    body += `<rect x="${P}" y="${y}" width="${iw}" height="1" fill="${C.track}"/>`
  }
  const caps = (x: number, yy: number, t: string) => txt(x, yy, t, 12, C.dim, 'font-weight="700" letter-spacing="1.5"')

  // Header: chấm trạng thái, tên, chip model · effort của luồng chính, thời điểm cập nhật
  body += `<circle cx="${P + 5}" cy="${y + 8}" r="5" fill="${C.g2}"/>`
  body += txt(P + 18, y + 13, 'CLAUDE USAGE', 13, C.dim, 'font-weight="700" letter-spacing="2"')
  if (v.mainModel) {
    const chip = `${prettyModel(v.mainModel)}${v.mainEffort ? ' · ' + v.mainEffort : ''}`
    const cw = chip.length * 7 + 18
    const cx = P + 140
    body += `<rect x="${cx}" y="${y}" width="${cw}" height="18" rx="9" fill="${EFF_COLOR[v.mainEffort ?? ''] ?? C.violet}" opacity="1"/>`
    body += txt(cx + 9, y + 13, chip, 11, '#bbbbbb', 'font-weight="600"')
  }
  if (v.s) body += txt(W - P, y + 13, `${fmtAgo(v.now - v.s.at)} trước`, 11, C.faint, 'text-anchor="end"')
  y += 34

  // Hạn mức
  const limits = v.s?.limits ?? []
  if (limits.length === 0) {
    body += txt(P, y + 12, 'Chờ phản hồi đầu tiên để có số hạn mức…', 12, C.dim)
    y += 28
  }
  for (const l of limits) {
    const p = pace(l, v.now)
    const pct = l.percentUsed
    body += caps(P, y + 18, labelOf(l.kind))
    body += txt(W - P, y + 24, `${Math.round(pct)}%`, 26, heat(pct), 'text-anchor="end" font-weight="700"')
    y += 34
    body += bar(P, y, iw, 12, pct, pct >= 85 ? 'gr' : pct >= 65 ? 'ga' : 'gg', { pace: p.expected })
    y += 26
    const meta: string[] = []
    if (p.resetIn !== undefined) meta.push(`reset ${fmtDur(p.resetIn)}`)
    if (p.ratePerUnit !== undefined) meta.push(`${p.ratePerUnit.toFixed(p.ratePerUnit < 10 ? 1 : 0)}%/${p.unit}`)
    const warn = p.fullIn !== undefined && p.resetIn !== undefined && p.fullIn < p.resetIn
    if (warn && p.fullIn !== undefined) meta.push(`đầy sau ${fmtDur(p.fullIn)}`)
    if (p.expected !== undefined) {
      const d = Math.round(pct - p.expected)
      meta.push(`${d >= 0 ? '+' : ''}${d}% so nhịp`)
    }
    body += txt(P, y, meta.join(' · '), 12, warn ? C.amber : C.dim)
    y += 22
  }

  // Context với vạch auto-compact
  const s = v.s
  if (s?.contextPercent !== undefined) {
    y += 4
    rule()
    y += 16
    body += caps(P, y + 12, 'CONTEXT')
    body += txt(W - P, y + 14, `${s.contextPercent}%`, 17, heat(s.contextPercent), 'text-anchor="end" font-weight="700"')
    y += 24
    const acPct =
      s.isAutoCompact && s.autoCompactAt && s.contextWindow ? (s.autoCompactAt / s.contextWindow) * 100 : undefined
    body += bar(P, y, iw, 8, s.contextPercent, s.contextPercent >= 85 ? 'gr' : 'gb', { marks: [], flag: acPct })
    y += 22
    body += txt(P, y, `${fmtTok(s.contextTokens ?? 0)} / ${fmtTok(s.contextWindow)}`, 11, C.dim)
    const right =
      acPct !== undefined
        ? `auto-compact ${fmtTok(s.autoCompactAt ?? 0)}`
        : s.isAutoCompact === false
          ? 'auto-compact tắt'
          : ''
    if (right) body += txt(W - P, y, right, 11, C.amber, 'text-anchor="end"')
    y += 18
  }

  // Chi phí: 4 ô số
  y += 4
  rule()
  y += 14
  const tiles: Array<[string, string]> = [
    [fmtUsd(v.total), 'phiên'],
    [fmtUsd(v.burn), '/ giờ'],
    [fmtUsd(v.hist.today), 'hôm nay'],
    [fmtUsd(v.hist.week), '7 ngày'],
  ]
  const tw = iw / 4
  tiles.forEach(([val, lab], i) => {
    const tx = P + i * tw
    body += txt(tx, y + 20, val, 17, C.text, 'font-weight="700"')
    body += txt(tx, y + 36, lab, 11, C.faint)
  })
  y += 48

  // Sparkline chi phí phiên
  if (v.spark.length >= 2) {
    const sh = 34
    const first = v.spark[0]
    const last = v.spark[v.spark.length - 1]
    if (first && last && last[0] - first[0] > 60_000 && last[1] > first[1]) {
      const t0 = first[0]
      const t1 = Math.max(t0 + 1, last[0])
      const vals = v.spark.map(p => p[1])
      const vmax = Math.max(...vals)
      const vmin = Math.min(...vals)
      const pts = v.spark.map(([t, c]) => {
        const px = P + ((t - t0) / (t1 - t0)) * iw
        const py = y + sh - ((c - vmin) / Math.max(1e-9, vmax - vmin)) * (sh - 4)
        return `${px.toFixed(1)},${py.toFixed(1)}`
      })
      body += `<polygon points="${P},${y + sh} ${pts.join(' ')} ${P + iw},${y + sh}" fill="url(#gs)"/>`
      body += `<polyline points="${pts.join(' ')}" fill="none" stroke="${C.blue}" stroke-width="2" stroke-linejoin="round"/>`
      y += sh + 10
    }
  }

  // Token: thanh xếp chồng theo USD + chú thích 2 cột
  const tokSum = v.split.input + v.split.output + v.split.cacheWrite + v.split.cacheRead
  if (tokSum > 0) {
    body += caps(P, y + 12, 'TOKEN')
    body += txt(W - P, y + 12, `cache hit ${Math.round(v.cacheHit)}%`, 11, C.g2, 'text-anchor="end"')
    y += 20
    const usdSum = v.splitUsd.input + v.splitUsd.output + v.splitUsd.cacheWrite + v.splitUsd.cacheRead || 1
    let x = P
    body += `<clipPath id="ct"><rect x="${P}" y="${y}" width="${iw}" height="10" rx="5"/></clipPath><g clip-path="url(#ct)">`
    for (const [k, , col] of SPLIT) {
      const w = (v.splitUsd[k] / usdSum) * iw
      body += `<rect x="${x.toFixed(1)}" y="${y}" width="${Math.max(0, w).toFixed(1)}" height="10" fill="${col}"/>`
      x += w
    }
    body += `</g>`
    y += 26
    const cw = iw / 2
    SPLIT.forEach(([k, lab, col], i) => {
      const cx = P + (i % 2) * cw
      const cy = y + Math.floor(i / 2) * 18
      body += `<circle cx="${cx + 4}" cy="${cy - 4}" r="4" fill="${col}"/>`
      body += txt(cx + 13, cy, `${lab} ${fmtTok(v.split[k])}`, 11, C.dim)
      body += txt(cx + cw - 10, cy, fmtUsd(v.splitUsd[k]), 11, C.text, 'text-anchor="end" font-weight="600"')
    })
    y += 34
  }

  // Theo model: chấm, tên, chip effort, thanh tỉ trọng, % · $
  if (v.models.length > 0) {
    rule()
    y += 16
    const nameW = Math.min(170, iw * 0.45)
    const valW = 96
    const bw = Math.max(40, iw - nameW - valW - 8)
    for (const m of v.models.slice(0, 6)) {
      body += `<circle cx="${P + 4}" cy="${y + 6}" r="4" fill="${C.blue}"/>`
      body += txt(P + 14, y + 10, m.name, 13, C.text, 'font-weight="600"')
      if (m.effort) {
        const ex = P + 14 + m.name.length * 8 + 6
        const ew = m.effort.length * 6.2 + 10
        body += `<rect x="${ex.toFixed(1)}" y="${y - 1}" width="${ew.toFixed(1)}" height="14" rx="7" fill="${EFF_COLOR[m.effort] ?? C.violet}" opacity="1"/>`
        body += txt(ex + 5, y + 9, m.effort, 10, '#bbbbbb', 'font-weight="600"')
      }
      body += bar(P + nameW, y + 2, bw, 7, m.share * 100, 'gb', { marks: [] })
      body += txt(W - P, y + 10, `${Math.round(m.share * 100)}% · ${fmtUsd(m.usd)}`, 12, C.blue, 'text-anchor="end" font-weight="700"')
      y += 24
    }
  }

  y += P - 6
  const H = Math.ceil(y)
  const defs = `<defs>
<linearGradient id="gg" x1="0" x2="1"><stop offset="0" stop-color="${C.g1}"/><stop offset="1" stop-color="${C.g2}"/></linearGradient>
<linearGradient id="ga" x1="0" x2="1"><stop offset="0" stop-color="${C.g2}"/><stop offset="1" stop-color="${C.amber}"/></linearGradient>
<linearGradient id="gr" x1="0" x2="1"><stop offset="0" stop-color="${C.amber}"/><stop offset="1" stop-color="${C.red}"/></linearGradient>
<linearGradient id="gb" x1="0" x2="1"><stop offset="0" stop-color="#4a6d8c"/><stop offset="1" stop-color="${C.blue}"/></linearGradient>
<linearGradient id="gs" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="${C.blue}" stop-opacity="0.22"/><stop offset="1" stop-color="${C.blue}" stop-opacity="0"/></linearGradient>
</defs>`
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="JetBrains Mono, Cascadia Mono, Consolas, monospace">${defs}<rect x="0" y="0" width="${W}" height="${H}" rx="6" fill="${C.bg}" stroke="${C.track}" stroke-width="1"/>${body}</svg>`
  return { svg, H }
}

// ---------- Bản chữ (terminal, vscode, mobile) ----------
function barText(pct: number, expected: number | undefined, cols: number, flag?: number) {
  const n = Math.max(8, cols)
  const filled = Math.round((Math.max(0, Math.min(100, pct)) / 100) * n)
  const mk = expected !== undefined ? Math.min(n - 1, Math.floor((expected / 100) * n)) : -1
  const fl = flag !== undefined ? Math.min(n - 1, Math.floor((flag / 100) * n)) : -1
  let s = ''
  for (let i = 0; i < n; i++) s += i === fl ? '◆' : i === mk ? '│' : i < filled ? '█' : '░'
  return s
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const r = await next(e)
    await $.command
      .register({ name: 'usage-pane', description: 'Mở pane Claude Usage (hạn mức + chi phí USD)' })
      .catch(() => undefined)
    await $.tool
      .register({
        name: 'snapshot',
        description:
          'Trả JSON số liệu thô mà pane Claude Usage đang hiển thị (hạn mức, context, auto-compact, chi phí, token, effort). Dùng để kiểm tra pane.',
        inputSchema: { type: 'object', properties: {} },
      })
      .catch(() => undefined)
    try {
      await take($, await $.session.usage(), true)
    } catch {
      /* chưa có số liệu */
    }
    await refreshHistory($, await $.clock.now()).catch(() => undefined)
    void $.ui.open({ id: PANE, title: TITLE })
    $.clock.every(30_000, () => $.ui.invalidate('ui.render'))
    return r
  })

  on('command.run', { command: 'usage-pane' }, async $ => {
    const o = await $.ui.open({ id: PANE, title: TITLE, focus: true })
    return { text: o.isPlaced ? 'Đã mở pane Claude Usage.' : 'Pane Claude Usage chưa hiển thị được ở giao diện này.' }
  })

  on('tool.call', { tool: 'mcp__usage-meter__snapshot' }, async $ => {
    const v = await view($)
    return { result: JSON.stringify({ ...v, localTime: new Date(v.now).toString() }, null, 1) }
  })

  on('session.measure', async ($, e, next) => {
    await take($, e, e.changed.includes('context')).catch(() => undefined)
    return next(e)
  })

  on('turn.step', async function* ($, e, next) {
    const eff = e.effort === undefined ? undefined : String(e.effort)
    await update($, effA, m => ({
      main: e.agentId === undefined ? e.model : m.main,
      byModel: eff ? { ...m.byModel, [e.model]: eff } : m.byModel,
    })).catch(() => undefined)
    return yield* next(e)
  })

  on('turn.complete', async ($, e, next) => {
    const r = await next(e)
    const u = e.usage
    if (u) await update($, tokA, m => addTok(m, u.model || 'unknown', u))
    await exportJson($, true)
    return r
  })

  on('ui.render', { component: 'Pane', requestId: PANE }, async ($, e) => {
    const table = $.ui.resolve(e)
    const { Box, Text } = table
    const v = await view($)
    const cols = Math.max(24, (e.props as { bodyColumns?: number }).bodyColumns ?? e.viewport?.columns ?? 40)

    if (e.surface === 'desktop' && 'Svg' in table) {
      const { Svg } = table as Elements['desktop']
      const W = Math.max(280, Math.min(640, cols * 8 - 8))
      const { svg, H } = buildSvg(v, W)
      const alt = `Claude Usage: ${(v.s?.limits ?? []).map(l => `${labelOf(l.kind)} ${Math.round(l.percentUsed)}%`).join(', ')}; ${fmtUsd(v.total)}`
      return (
        <Box flexDirection="column">
          <Svg source={svg} alt={alt} width={W} height={H} />
        </Box>
      )
    }

    // Bản chữ gọn cho terminal, vscode, mobile
    const s = v.s
    const w = cols - 2
    const acPct = s && s.isAutoCompact && s.autoCompactAt ? (s.autoCompactAt / s.contextWindow) * 100 : undefined
    return (
      <Box flexDirection="column">
        {(s?.limits ?? []).map(l => {
          const p = pace(l, v.now)
          const meta = [
            p.resetIn !== undefined ? `reset ${fmtDur(p.resetIn)}` : '',
            p.ratePerUnit !== undefined ? `${p.ratePerUnit.toFixed(1)}%/${p.unit}` : '',
          ]
            .filter(Boolean)
            .join(' · ')
          return (
            <Box flexDirection="column">
              <Box flexDirection="row" justifyContent="space-between">
                <Text dimColor>{labelOf(l.kind)}</Text>
                <Text weight="bold" color={heat(l.percentUsed)}>{`${Math.round(l.percentUsed)}%`}</Text>
              </Box>
              <Text color={heat(l.percentUsed)}>{barText(l.percentUsed, p.expected, w)}</Text>
              <Text dimColor>{meta || ' '}</Text>
            </Box>
          )
        })}
        {s?.contextPercent !== undefined ? (
          <Box flexDirection="column">
            <Text dimColor>{`CONTEXT ${s.contextPercent}% · ${fmtTok(s.contextTokens ?? 0)}/${fmtTok(s.contextWindow)}${acPct !== undefined ? ` · ◆ compact ${fmtTok(s.autoCompactAt ?? 0)}` : ''}`}</Text>
            <Text color={heat(s.contextPercent)}>{barText(s.contextPercent, undefined, w, acPct)}</Text>
          </Box>
        ) : null}
        <Text>{`${fmtUsd(v.total)} phiên · ${fmtUsd(v.burn)}/h · ${fmtUsd(v.hist.today)} hôm nay · ${fmtUsd(v.hist.week)} 7d`}</Text>
        {v.models.slice(0, 6).map(m => (
          <Text dimColor>{`${m.name}${m.effort ? ` [${m.effort}]` : ''} ${Math.round(m.share * 100)}% · ${fmtUsd(m.usd)}`}</Text>
        ))}
      </Box>
    )
  })
}
