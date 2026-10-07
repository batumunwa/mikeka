import { useEffect, useState, type FormEvent, type KeyboardEvent } from 'react'
import { api, MARKET_NAMES, SITE_NAMES, SITE_URLS, siteOfUrl, type Account, type MarketChoice, type MarketKey, type SaveAccount, type Settings, type Site } from './api'
import { Modal } from './ui'

// Interval dropdowns: 5-minute steps cover the site's intervals (1–10, 1–15, 16–30, …, 86–90).
const START_MINUTES = Array.from({ length: 18 }, (_, k) => 1 + k * 5)
const END_MINUTES = Array.from({ length: 18 }, (_, k) => 5 + k * 5)

const ordinal = (n: number) => `${n}${n === 1 ? 'st' : n === 2 ? 'nd' : n === 3 ? 'rd' : 'th'}`

/** Tag input for league names: Enter, comma or leaving the box adds the typed league. */
function LeagueTags({ leagues, onChange, label }: { leagues: string[]; onChange: (l: string[]) => void; label: string }) {
  const [text, setText] = useState('')
  const add = () => {
    // Several leagues typed or pasted at once ("A. X, B. Y") become separate tags.
    const next = [...leagues]
    for (const l of text.split(/[,;]/).map((x) => x.trim()).filter(Boolean))
      if (!next.some((x) => x.toLowerCase() === l.toLowerCase())) next.push(l)
    if (next.length !== leagues.length) onChange(next)
    setText('')
  }
  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault()
      add()
    }
  }
  return (
    <div className="tags">
      {leagues.map((l) => (
        <span key={l} className="tag">
          {l}
          <button type="button" aria-label={`Remove ${l}`} onClick={() => onChange(leagues.filter((x) => x !== l))}>×</button>
        </span>
      ))}
      <input value={text} aria-label={label} onChange={(e) => setText(e.target.value)} onKeyDown={onKey} onBlur={add}
        placeholder={leagues.length ? 'Add another league…' : 'Country. League, as named on the site, e.g. Argentina. Liga Profesional'} />
    </div>
  )
}

interface Props {
  settings?: Settings | null
  account?: Account
  onSaved: (a: Account) => void
  onCancel: () => void
}

export function AccountForm({ settings, account, onSaved, onCancel }: Props) {
  const [form, setForm] = useState<SaveAccount>({
    name: account?.name ?? '',
    url: account?.url ?? SITE_URLS['1win'],
    site: account?.site ?? '1win',
    username: account?.username ?? '',
    password: '',
    markets: account?.markets?.length ? account.markets : [{ market: 'corners', side: 'Under', leagues: [] }],
    currency: account?.currency ?? 'TZS',
    baseStake: account?.baseStake ?? 1000,
    // A new account starts from the Settings values; each account can then have its own.
    maxLosses: account?.maxLosses ?? settings?.maxLosses ?? 4,
    minPickOdds: account?.minPickOdds ?? settings?.minPickOdds ?? 1.1,
    maxPickOdds: account?.maxPickOdds ?? settings?.maxPickOdds ?? 1.2,
    minCombinedOdds: account?.minCombinedOdds ?? settings?.minCombinedOdds ?? 2.1,
    maxCombinedOdds: account?.maxCombinedOdds ?? settings?.maxCombinedOdds ?? 2.2,
    maxPicks: account?.maxPicks ?? settings?.maxMatches ?? 6,
    isActive: account?.isActive ?? true,
  })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const pickRange = form.minPickOdds && form.maxPickOdds ? `${form.minPickOdds.toFixed(2)}–${form.maxPickOdds.toFixed(2)}` : 'in the set range'
  const maxMatches = form.maxPicks || 1
  const best = Math.pow(form.maxPickOdds || 0, maxMatches)

  // Settings may arrive after the form opened: a new account then takes their values.
  useEffect(() => {
    if (account || !settings) return
    setForm((f) => ({
      ...f, maxLosses: settings.maxLosses, minPickOdds: settings.minPickOdds, maxPickOdds: settings.maxPickOdds,
      minCombinedOdds: settings.minCombinedOdds, maxCombinedOdds: settings.maxCombinedOdds,
    }))
  }, [account, settings])

  // Editing: put the saved password in the box (hidden until Show) so it can be checked.
  useEffect(() => {
    if (!account) return
    api.savedPassword(account.id)
      .then((r) => setForm((f) => ({ ...f, password: r.password })))
      .catch(() => setError('Could not load the saved password.'))
  }, [account])

  const set = <K extends keyof SaveAccount>(k: K, v: SaveAccount[K]) => setForm((f) => ({ ...f, [k]: v }))

  const update = (i: number, patch: Partial<MarketChoice>) =>
    setForm((f) => ({ ...f, markets: f.markets.map((x, j) => (j === i ? { ...x, ...patch } : x)) }))

  const move = (i: number, by: number) => {
    const list = [...form.markets]
    ;[list[i], list[i + by]] = [list[i + by], list[i]]
    set('markets', list)
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (form.markets.length === 0) return setError('Choose at least one market.')
    const noLeague = form.markets.findIndex((m) => m.leagues.length === 0)
    if (noLeague >= 0) return setError(`Add at least one league to the ${ordinal(noLeague + 1)} market.`)
    if (!(form.baseStake > 0)) return setError('Base stake must be more than 0.')
    if (!(form.maxLosses >= 1)) return setError('Maximum losses must be at least 1.')
    if (!(form.maxPicks >= 1 && form.maxPicks <= 20)) return setError('Maximum picks per slip must be between 1 and 20.')
    if (!(form.maxPickOdds > form.minPickOdds)) return setError('Maximum pick odds must be higher than the minimum.')
    if (!(form.maxCombinedOdds > form.minCombinedOdds)) return setError('Maximum combined odds must be higher than the minimum.')
    if (!account && !form.password) return setError('Password is required.')
    setSaving(true)
    setError(null)
    try {
      const body = { ...form, password: form.password || undefined }
      onSaved(account ? await api.updateAccount(account.id, body) : await api.createAccount(body))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const oddsInput = (k: 'minPickOdds' | 'maxPickOdds' | 'minCombinedOdds' | 'maxCombinedOdds', label: string) => (
    <label>
      {label}
      <input type="number" step="0.01" min="1.01" value={form[k] || ''} required
        onChange={(e) => set(k, e.target.value === '' ? 0 : +e.target.value)} />
    </label>
  )

  return (
    <Modal title={account ? `Edit ${account.name}` : 'Register account'} width={820} dismissable={false} onClose={onCancel}
      subtitle="Login, markets in order, leagues, stake and limits"
      footer={<>
        {error && <p className="error foot-error">{error}</p>}
        <button type="button" className="ghost" onClick={onCancel}>Cancel</button>
        <button type="submit" form="account-form" disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
      </>}>
    <form id="account-form" className="form" onSubmit={submit} autoComplete="off">
      <label>
        Name
        <input value={form.name} onChange={(e) => set('name', e.target.value)} required placeholder="e.g. Main account" />
      </label>
      <div className="row2">
        <label>
          Betting site
          <select value={form.site} onChange={(e) => {
            const site = e.target.value as Site
            set('site', site)
            // Keep an address the user typed for this same site (e.g. a new mirror); otherwise use the site's default.
            if (siteOfUrl(form.url) !== site) set('url', SITE_URLS[site])
          }}>
            {(Object.keys(SITE_NAMES) as Site[]).map((s) => <option key={s} value={s}>{SITE_NAMES[s]}</option>)}
          </select>
        </label>
        <label>
          Site URL
          <input type="url" value={form.url} required
            onChange={(e) => {
              const url = e.target.value
              set('url', url)
              const site = siteOfUrl(url)
              if (site) set('site', site)
            }} />
        </label>
      </div>
      <div className="row2">
        <label>
          Username
          <input value={form.username} onChange={(e) => set('username', e.target.value)} required
            name="mikeka-site-user" autoComplete="off" data-lpignore="true" data-1p-ignore data-form-type="other" />
        </label>
        <label>
          Password {account && <span className="muted">(saved password; click Show to check it)</span>}
          <span className="password-box">
            {/* "one-time-code" stops the browser filling in saved passwords or offering to generate one. */}
            <input type={showPassword ? 'text' : 'password'} value={form.password} onChange={(e) => set('password', e.target.value)}
              name="mikeka-site-secret" autoComplete="one-time-code" data-lpignore="true" data-1p-ignore data-form-type="other"
              spellCheck={false} autoCapitalize="off" />
            <button type="button" className="ghost" onClick={() => setShowPassword((v) => !v)}
              aria-label={showPassword ? 'Hide password' : 'Show password'}>
              {showPassword ? 'Hide' : 'Show'}
            </button>
          </span>
        </label>
      </div>
      <div className="markets-field">
        <span>
          Markets to bet on, in order{' '}
          <span className="muted">
            (always Under. For each match, only markets listing that match's league are tried: the 1st with odds {pickRange} is used,
            else the 2nd, …; none = no bet on that match. Under value: optional exact line, e.g. 0.5; empty = the highest line with
            odds {pickRange}, or 0.5 ("none") with a time interval.)
          </span>
        </span>
        {form.markets.map((m, i) => (
          <div key={i} className="market-block">
            <div className="market-row">
              <span className="order">{ordinal(i + 1)} choice</span>
              <select value={m.market} aria-label={`Market ${i + 1}`}
                onChange={(e) => {
                  const market = e.target.value as MarketKey
                  // Result: Draw = level score after minute N (from kickoff), so it always has an interval from minute 1 and no Under value.
                  update(i, market === 'result'
                    ? { market, side: 'Draw', line: null, intervalFrom: 1, intervalTo: m.intervalTo && m.intervalTo > 1 ? m.intervalTo : 10 }
                    : { market, side: 'Under' })
                }}>
                {(Object.keys(MARKET_NAMES) as MarketKey[]).map((k) => (
                  <option key={k} value={k}>{MARKET_NAMES[k]}</option>
                ))}
              </select>
              <span className="side">{m.market === 'result' ? 'Draw' : 'Under'}</span>
              {m.market !== 'result' && (
                <input className="line-input" type="number" min="0.5" step="0.5" aria-label={`Under value ${i + 1}`}
                  placeholder={m.intervalFrom != null ? '0.5' : 'auto'} value={m.line ?? ''}
                  onChange={(e) => update(i, { line: e.target.value === '' ? null : +e.target.value })} />
              )}
              <span className="interval">
                <select value={m.intervalFrom ?? ''} aria-label={`Interval start ${i + 1}`} disabled={m.market === 'result'}
                  onChange={(e) => update(i, e.target.value
                    ? { intervalFrom: +e.target.value, intervalTo: m.intervalTo && m.intervalTo > +e.target.value ? m.intervalTo : Math.min(+e.target.value + 9, 90) }
                    : { intervalFrom: null, intervalTo: null })}>
                  {m.market !== 'result' && <option value="">Whole match</option>}
                  {(m.market === 'result' ? [1] : START_MINUTES).map((n) => <option key={n} value={n}>From {n}′</option>)}
                </select>
                {m.intervalFrom != null && (
                  <select value={m.intervalTo ?? ''} aria-label={`Interval end ${i + 1}`}
                    onChange={(e) => update(i, { intervalTo: +e.target.value })}>
                    {END_MINUTES.filter((n) => n > (m.intervalFrom ?? 0)).map((n) => <option key={n} value={n}>to {n}′</option>)}
                  </select>
                )}
              </span>
              <button type="button" className="ghost icon" aria-label="Move up" disabled={i === 0} onClick={() => move(i, -1)}>▲</button>
              <button type="button" className="ghost icon" aria-label="Move down" disabled={i === form.markets.length - 1} onClick={() => move(i, 1)}>▼</button>
              {form.markets.length > 1 && (
                <button type="button" className="ghost" aria-label="Remove market"
                  onClick={() => set('markets', form.markets.filter((_, j) => j !== i))}>Remove</button>
              )}
            </div>
            <div className="market-leagues">
              <span className="muted small">Leagues for this market</span>
              <LeagueTags leagues={m.leagues} label={`Leagues for market ${i + 1}`} onChange={(l) => update(i, { leagues: l })} />
            </div>
          </div>
        ))}
        <button type="button" className="ghost add-market"
          onClick={() => {
            const free = (Object.keys(MARKET_NAMES) as MarketKey[]).find((k) => !form.markets.some((x) => x.market === k)) ?? 'goals'
            const leagues = form.markets[form.markets.length - 1]?.leagues ?? [] // start with the previous row's leagues
            set('markets', [...form.markets, { market: free, side: 'Under', leagues: [...leagues] }])
          }}>+ Add market</button>
      </div>
      <div className="row2">
        <label>
          Base stake <span className="muted">({form.currency || 'TZS'})</span>
          <input type="number" min="1" step="any" value={form.baseStake || ''} required
            onChange={(e) => set('baseStake', +e.target.value)} />
          <span className="muted small">
            Doubles after each loss: {Array.from({ length: Math.min(Math.max(form.maxLosses || 1, 1), 8) }, (_, k) => ((form.baseStake || 0) * 2 ** k).toLocaleString()).join(' → ')}; back to {(form.baseStake || 0).toLocaleString()} after a win.
          </span>
        </label>
        <label>
          Maximum losses in a row
          <input type="number" min="1" step="1" value={form.maxLosses || ''} required
            onChange={(e) => set('maxLosses', +e.target.value)} />
          <span className="muted small">Betting stops after {form.maxLosses || '?'} lost slips in a row; an email warns one loss before.</span>
        </label>
      </div>
      <fieldset className="limits">
        <legend>Odds per pick</legend>
        <div className="row2">
          {oddsInput('minPickOdds', 'Minimum')}
          {oddsInput('maxPickOdds', 'Maximum')}
        </div>
      </fieldset>
      <fieldset className="limits">
        <legend>Combined odds of the slip</legend>
        <div className="row2">
          {oddsInput('minCombinedOdds', 'Minimum')}
          {oddsInput('maxCombinedOdds', 'Maximum')}
        </div>
        <div className="row2">
          <label>
            Maximum picks per slip
            <input type="number" min="1" max="20" step="1" value={form.maxPicks || ''} required
              onChange={(e) => set('maxPicks', +e.target.value)} />
          </label>
        </div>
        <p className="muted small">
          Up to {maxMatches} picks. With picks up to {(form.maxPickOdds || 0).toFixed(2)}, the highest possible slip is {best.toFixed(2)}
          {best < form.minCombinedOdds && <b className="neg"> — below the minimum, so no slip could ever be built.</b>}
          {!account && <> New accounts start with the values from Settings.</>}
        </p>
      </fieldset>
      <div className="row2">
        <label>
          Currency
          <input value={form.currency} onChange={(e) => set('currency', e.target.value)} required />
        </label>
        <label className="check">
          <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} />
          Active (checked automatically)
        </label>
      </div>
      <p className="muted small">The password is encrypted before it is stored and is never shown again.</p>
    </form>
    </Modal>
  )
}
