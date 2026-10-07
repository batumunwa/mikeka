export type MarketKey = 'goals' | 'corners' | 'cards' | 'fouls' | 'result'

export interface MarketChoice {
  market: MarketKey
  /** "Under" for totals; "Draw" for the result market. */
  side: 'Under' | 'Draw'
  /** Leagues where this market is used, as named on the site. */
  leagues: string[]
  /** Optional interval in minutes; when set the bet is "no event" in that interval. */
  /** Optional exact Under value, e.g. 0.5. Empty = automatic (whole match: highest line in range; interval: 0.5). */
  line?: number | null
  intervalFrom?: number | null
  intervalTo?: number | null
}

/** "Corners · Under" or "Goals · none in 1–10 min". */
export const choiceText = (m: { market: MarketKey; side: string; line?: number | null; intervalFrom?: number | null; intervalTo?: number | null }) => {
  if (m.market === 'result') return `Result · draw after minute ${m.intervalTo ?? '?'}`
  const name = MARKET_NAMES[m.market] ?? m.market
  const under = m.line ? `${m.side} ${m.line}` : m.intervalFrom ? `${m.side} 0.5` : `${m.side} (auto)`
  return m.intervalFrom && m.intervalTo ? `${name} · ${under} in ${m.intervalFrom}–${m.intervalTo} min` : `${name} · ${under}`
}

/** Pick text: "Total corners Under 9.5" or "No goals in minutes 1–10". */
export const pickText = (p: { market: MarketKey; side: string; line: number; interval?: string | null; label?: string | null }) =>
  p.label
    ? p.label
    : p.market === 'result'
    ? `Draw after minute ${p.interval?.split('-').pop() ?? '?'}`
    : p.interval
    ? p.line === 0.5
      ? `No ${(MARKET_NAMES[p.market] ?? p.market).toLowerCase()} in minutes ${p.interval.replace('-', '–')}`
      : `${(MARKET_NAMES[p.market] ?? p.market)} ${p.side} ${p.line} in minutes ${p.interval.replace('-', '–')}`
    : `Total ${(MARKET_NAMES[p.market] ?? p.market).toLowerCase()} ${p.side} ${p.line}`

export const MARKET_NAMES: Record<MarketKey, string> = {
  goals: 'Goals',
  corners: 'Corners',
  cards: 'Yellow cards',
  fouls: 'Fouls',
  result: 'Result: Draw',
}

export type Site = 'coldbet' | '1win' | 'leonbet'
export const SITE_NAMES: Record<Site, string> = { coldbet: 'Coldbet', '1win': '1win', leonbet: 'Leonbet' }
/** Default address filled in when a site is chosen. */
export const SITE_URLS: Record<Site, string> = {
  coldbet: 'https://coldbet1f.com/en/line/football',
  '1win': 'https://1wnorh.life',
  leonbet: 'https://leonbet.co.tz',
}

/** Which site an address belongs to, or null if it can't be told. */
export const siteOfUrl = (url: string): Site | null =>
  /\/\/(1w|[^/]*1win)/i.test(url) ? '1win' : /leonbet/i.test(url) ? 'leonbet' : /coldbet/i.test(url) ? 'coldbet' : null

export interface Account {
  id: number
  name: string
  url: string
  site: Site
  username: string
  currency: string
  isActive: boolean
  leagues: string[]
  markets: MarketChoice[]
  lossStreak: number
  stopped: boolean
  lastBalance: number | null
  nextStake: number
  baseStake: number
  maxLosses: number
  minPickOdds: number
  maxPickOdds: number
  minCombinedOdds: number
  maxCombinedOdds: number
  /** When the system next checks this account (UTC ISO); null = as soon as possible. */
  nextCheckAt: string | null
}

export interface SaveAccount {
  name: string
  url: string
  site: Site
  username: string
  password?: string
  markets: MarketChoice[]
  currency: string
  isActive: boolean
  baseStake: number
  /** Losses in a row that stop betting; defaults from Settings. */
  maxLosses: number
  /** This account's odds ranges; default from Settings. */
  minPickOdds: number
  maxPickOdds: number
  minCombinedOdds: number
  maxCombinedOdds: number
}

export interface Pick {
  league: string
  home: string
  away: string
  kickoff: string
  market: MarketKey
  side: string
  line: number
  odds: number
  interval?: string | null
  /** The site's own market and outcome, e.g. "10 Minute Result: X (draw)". */
  label?: string | null
}

export type SlipStatus = 'Pending' | 'Won' | 'Lost' | 'Skipped' | 'Draft'

export interface Slip {
  id: number
  accountId: number
  betDay: string
  createdAt: string
  settledAt: string | null
  status: SlipStatus
  stake: number
  combinedOdds: number
  potentialReturn: number
  betReference: string | null
  balanceBefore: number | null
  balanceAfter: number | null
  note: string | null
  /** When the system reads the slip's result (UTC ISO): after a gap between match ends, and at the last end. */
  settlementChecks: string[]
  resultCheckedAt: string | null
  picks: Pick[]
}

export interface RunLog {
  id: number
  accountId: number | null
  at: string
  level: string
  message: string
}

export interface BalancePoint {
  at: string
  balance: number
}

export interface MatchPreview {
  league: string
  home: string
  away: string
  kickoff: string
  url: string
  selections: { market: MarketKey; side: string; line: number; odds: number; interval?: string | null }[]
  pick: { market: MarketKey; side: string; line: number; odds: number; interval?: string | null } | null
}

export interface MatchesResult {
  ok: boolean
  message: string
  matches: MatchPreview[]
}

export interface AnalysedMatch {
  league: string
  home: string
  away: string
  kickoff: string
  url: string
  steps: string[]
  pick: string | null
  pickOdds: number | null
  inSlip: boolean
}

export interface AnalysisReport {
  fromEat: string
  toEat: string
  lastKnownBalance: number | null
  lossStreak: number
  stake: number
  currency: string
  matches: AnalysedMatch[]
  combinedOdds: number | null
  potentialReturn: number | null
  chancePercent: number | null
  canCreateSlip: boolean
  verdict: string
}

export interface Settings {
  minPickOdds: number
  maxPickOdds: number
  minCombinedOdds: number
  maxCombinedOdds: number
  updatedAt: string
  maxMatches: number
  /** True: "Run now" builds slips but never places them. */
  dryRun: boolean
  /** True: runs click Place themselves (real money); false: they fill the slip and you place it. */
  placeBets: boolean
  /** Default maximum losses in a row for new accounts. */
  maxLosses: number
  /** Matches with these teams are never picked (all accounts). */
  excludedTeams: string[]
  /** Each account is checked again this long after its last check. */
  checkIntervalMinutes: number
  /** On one company, the next account starts this long after the previous one finished. */
  sameSiteDelayMinutes: number
  /** A match is assumed over this long after kickoff. */
  matchMinutes: number
  /** Match ends further apart than this get separate result checks. */
  settlementGapMinutes: number
  /** Matches are looked for over this many days from now. */
  maxDaysAhead: number
}

export interface RunResult {
  accountId: number
  outcome: string
  message: string
  slipId: number | null
}

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`/api${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      msg = body.detail ?? body.title ?? (typeof body === 'string' ? body : msg)
      if (body.errors) msg += ': ' + Object.values(body.errors).flat().join(' ')
    } catch { /* not JSON */ }
    throw new Error(msg)
  }
  return res.status === 204 ? (undefined as T) : res.json()
}

export const api = {
  accounts: () => call<Account[]>('/accounts'),
  createAccount: (a: SaveAccount) => call<Account>('/accounts', { method: 'POST', body: JSON.stringify(a) }),
  updateAccount: (id: number, a: SaveAccount) => call<Account>(`/accounts/${id}`, { method: 'PUT', body: JSON.stringify(a) }),
  savedPassword: (id: number) => call<{ password: string }>(`/accounts/${id}/password`),
  resetAccount: (id: number) => call<Account>(`/accounts/${id}/reset`, { method: 'POST' }),
  disableAccount: (id: number) => call<void>(`/accounts/${id}`, { method: 'DELETE' }),
  check: (id: number) => call<{ ok: boolean; balance: number | null; message: string }>(`/accounts/${id}/check`, { method: 'POST' }),
  readMatches: (id: number) => call<MatchesResult>(`/accounts/${id}/matches`, { method: 'POST' }),
  analysisWindow: (id: number) => call<{ from: string; to: string }>(`/accounts/${id}/analysis/window`),
  analyse: (id: number, from: string, to: string) =>
    call<AnalysisReport | { error: string }>(`/accounts/${id}/analysis?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, { method: 'POST' }),
  createSlip: (id: number) => call<{ id: number; stake: number; combinedOdds: number; picks: number }>(`/accounts/${id}/analysis/create-slip`, { method: 'POST' }),
  run: (id: number) => call<RunResult>(`/run/${id}`, { method: 'POST' }),
  slips: (accountId?: number) => call<Slip[]>(`/slips${accountId ? `?accountId=${accountId}` : ''}`),
  logs: (accountId?: number) => call<RunLog[]>(`/logs${accountId ? `?accountId=${accountId}` : ''}`),
  balanceHistory: (id: number) => call<BalancePoint[]>(`/accounts/${id}/balance-history`),
  totalBalanceHistory: () => call<BalancePoint[]>('/accounts/balance-history'),
  settings: () => call<Settings>('/settings'),
  saveSettings: (s: Settings) => call<Settings>('/settings', { method: 'PUT', body: JSON.stringify(s) }),
  settleSlip: (id: number, won: boolean) => call<void>(`/slips/${id}/settle?won=${won}`, { method: 'POST' }),
  deleteSlip: (id: number) => call<void>(`/slips/${id}`, { method: 'DELETE' }),
}

/** The bet is really on the site: it has the site's bet number, or it was settled Won/Lost. */
export const isPlaced = (s: Slip) => !!s.betReference || s.status === 'Won' || s.status === 'Lost'
