import { expect, mock, test } from 'claude-code/testing'

const PANE = {
  plugin: 'usage-meter',
  component: 'Pane',
  requestId: 'usage-meter',
  props: {
    title: 'Claude Usage',
    isFocused: false,
    bodyColumns: 52,
    placement: 'dock',
    scroll: { offset: 0, bodyRows: 60 },
    view: {},
  },
} as const

test('sổ chi phí: hôm nay = chi phí lần chạy hiện tại + phiên khác, bỏ khoá cũ bị hụt', async ($, on) => {
  const clk = mock.clock(on, { now: new Date(2026, 10, 11, 10, 0, 0).getTime() })
  const now = clk.now()
  const startedAt = now - 3600e3
  // khoá kiểu cũ của chính phiên s1 (delta bị hụt) phải bị bỏ; phiên khác cùng ngày phải được cộng
  mock.store(on, {
    'd:2026-11-11:s1': 1,
    'd:2026-11-11:s0:123': 1.25,
    'd:2026-11-09:s0:99': 4,
  })
  const usage = {
    startedAt,
    context: { tokens: 1000, window: 1_000_000, percent: 0 },
    rateLimits: [{ kind: 'five_hour', percentUsed: 10, resetsAt: new Date(now + 3600e3).toISOString() }],
    cost: { usd: 27.07 },
  }
  on('command.register', () => ({ value: { command: 'usage-pane' } }))
  on('tool.register', () => ({ value: {} }))
  on('ui.open', () => ({ value: { isPlaced: true } }))
  on('session.usage', () => ({ value: usage }))
  on('session.id', () => ({ value: 's1' }))
  on('session.start', (_, e) => ({ cwd: e.cwd }))
  await $.session.start({ cwd: '/', surface: 'terminal', isInteractive: true })

  const ui = await $.ui.mount({ ...PANE, surface: 'terminal' })
  const line = (await ui.find({ type: 'Text', text: /hôm nay/ }))?.text ?? ''
  // hôm nay = 27.07 + 1.25 = 28.32; 7 ngày thêm 4 của ngày 09
  expect(line).toContain('$27.07 phiên')
  expect(line).toContain('$28.32 hôm nay')
  expect(line).toContain('$32.32 7d')
  await ui.unmount()
})

test('pane: hạn mức, context có vạch auto-compact, effort theo model, chi phí', async ($, on) => {
  const clk = mock.clock(on, { now: Date.UTC(2026, 10, 11, 2, 0, 0) })
  mock.store(on)
  const now = clk.now()
  const iso = (ms: number) => new Date(now + ms).toISOString()
  const usage = {
    startedAt: now - 3600e3,
    context: {
      tokens: 313_000,
      window: 1_000_000,
      percent: 31,
      breakdown: { autoCompactThreshold: 417_000, isAutoCompactEnabled: true, rawMaxTokens: 450_000 },
    },
    rateLimits: [
      { kind: 'seven_day', percentUsed: 35, resetsAt: iso(5 * 86400e3 + 5 * 3600e3) },
      { kind: 'five_hour', percentUsed: 13, resetsAt: iso(76 * 60e3) },
    ],
    cost: { usd: 2.5 },
  }
  on('command.register', () => ({ value: { command: 'usage-pane' } }))
  on('tool.register', () => ({ value: {} }))
  on('ui.open', () => ({ value: { isPlaced: true } }))
  on('session.usage', () => ({ value: usage }))
  on('session.id', () => ({ value: 's1' }))
  on('session.start', (_, e) => ({ cwd: e.cwd }))
  on('turn.complete', (_, e) => ({ text: e.answer }))

  await $.session.start({ cwd: '/', surface: 'desktop', isInteractive: true })
  await $.turn.complete({
    answer: 'ok',
    durationMs: 1000,
    isAborted: false,
    turnId: 't1',
    reason: 'answer',
    usage: {
      model: 'claude-opus-5-5',
      input_tokens: 1000,
      output_tokens: 2000,
      cache_read_input_tokens: 100_000,
      cache_creation_input_tokens: 10_000,
    },
  })

  // Bản chữ
  for (const surface of ['terminal', 'vscode', 'mobile'] as const) {
    const ui = await $.ui.mount({ ...PANE, surface })
    expect((await ui.find({ type: 'Text', text: /^PHIÊN 5 GIỜ$/ }))?.text).toBeDefined()
    expect((await ui.find({ type: 'Text', text: /^13%$/ }))?.text).toBe('13%')
    expect((await ui.find({ type: 'Text', text: /CONTEXT 70% · 313k\/450k · ◆ compact 417k/ }))?.text).toBeDefined()
    expect((await ui.find({ type: 'Text', text: /\$2\.50 phiên/ }))?.text).toBeDefined()
    expect((await ui.find({ type: 'Text', text: /Opus 5\.5/ }))?.text).toBeDefined()
    await ui.unmount()
  }

  // Desktop: một SVG
  const ui = await $.ui.mount({ ...PANE, surface: 'desktop' })
  const svg = (await ui.find({ type: 'Svg' })) as unknown as { props?: { source?: string; alt?: string } } | undefined
  const src = svg?.props?.source ?? ''
  expect(src).toContain('PHIÊN 5 GIỜ')
  expect(src).toContain('auto-compact 417k')
  expect(src).toContain('>70%<')
  expect(src).toContain('reset 1h 16m')
  expect(src).toContain('$2.50')
  expect(src).toContain('Opus 5.5')
  await ui.unmount()
})
