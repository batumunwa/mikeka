import { useCallback, useEffect, useState } from 'react'
import { api, choiceText, isPlaced, pickText, SITE_NAMES, type Account, type BalancePoint, type MatchesResult, type Settings, type Slip } from './api'
import { BalanceChart } from './BalanceChart'
import { AccountForm } from './AccountForm'
import { AccountPanel } from './AccountPanel'
import { AnalysisPanel } from './AnalysisPanel'
import { SettingsPanel } from './SettingsPanel'
import { Modal, money, SITE_COLORS, SiteAvatar, Stat, StreakDots, useConfirm } from './ui'

const range = (a?: number, b?: number) => (a && b ? `${a.toFixed(2)}–${b.toFixed(2)}` : '…')
const eat = (iso: string) =>
  new Date(iso).toLocaleString('en-GB', { timeZone: 'Africa/Dar_es_Salaam', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })

export default function App() {
  const [accounts, setAccounts] = useState<Account[]>([])
  const [selected, setSelected] = useState<number | undefined>()
  const [slips, setSlips] = useState<Slip[]>([])
  const [refreshKey, setRefreshKey] = useState(0)
  const [editing, setEditing] = useState<Account | 'new' | null>(null)
  const [busy, setBusy] = useState<number | null>(null)
  const [analysing, setAnalysing] = useState<Account | null>(null)
  const [confirm, confirmDialog] = useConfirm()
  const [preview, setPreview] = useState<{ account: Account; result: MatchesResult } | null>(null)
  const [settings, setSettings] = useState<Settings | null>(null)
  const [showSettings, setShowSettings] = useState(false)
  const [notice, setNotice] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null)
  const [totalHistory, setTotalHistory] = useState<BalancePoint[] | null>(null)

  const load = useCallback(async () => {
    try {
      const [a, s] = await Promise.all([api.accounts(), api.slips()])
      setAccounts(a)
      setSlips(s)
      setRefreshKey((k) => k + 1) // the open account panel reloads its activity and balance too
    } catch (e) {
      setNotice({ kind: 'error', text: `Cannot reach the API: ${(e as Error).message}` })
    }
  }, [])

  useEffect(() => {
    api.settings().then(setSettings).catch(() => {})
  }, [])

  // An open "⋯" menu closes on a click anywhere outside it, or on Esc.
  useEffect(() => {
    const closeMenus = (keep?: Node) =>
      document.querySelectorAll<HTMLDetailsElement>('details.more[open]').forEach((d) => { if (!keep || !d.contains(keep)) d.open = false })
    const onPointer = (e: PointerEvent) => closeMenus(e.target as Node)
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') closeMenus() }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey)
    }
  }, [])

  useEffect(() => {
    load()
    const t = setInterval(load, 30_000)
    return () => clearInterval(t)
  }, [load])

  const act = async (id: number, fn: () => Promise<unknown>, done: (r: unknown) => string) => {
    setBusy(id)
    setNotice(null)
    try {
      setNotice({ kind: 'ok', text: done(await fn()) })
      await load()
    } catch (e) {
      setNotice({ kind: 'error', text: (e as Error).message })
    } finally {
      setBusy(null)
    }
  }


  const waitingAll = slips.filter((s) => s.status === 'Pending').length
  const today = new Date().toLocaleDateString('en-CA', { timeZone: 'Africa/Dar_es_Salaam' })
  const todays = slips.filter((s) => s.betDay === today && s.status !== 'Skipped').length
  const placedToday = slips.filter((s) => s.betDay === today && isPlaced(s))
  const totalBalance = accounts.reduce((sum, a) => sum + (a.lastBalance ?? 0), 0)
  const currencies = [...new Set(accounts.map((a) => a.currency))]

  const openTotalHistory = async () => {
    try {
      setTotalHistory(await api.totalBalanceHistory())
    } catch (e) {
      setNotice({ kind: 'error', text: (e as Error).message })
    }
  }

  return (
    <>
    {confirmDialog}
    <header className="hero">
      <div className="hero-inner">
        <div className="brand">
          <span className="logo" aria-hidden>
            <svg viewBox="0 0 24 24" width="22" height="22"><circle cx="12" cy="12" r="9.5" fill="none" stroke="currentColor" strokeWidth="1.8" /><path d="M12 7.2l3.3 2.4-1.3 3.9h-4l-1.3-3.9z" fill="currentColor" /><path d="M12 7.2V2.6M15.3 9.6l4.3-1.4M14 13.5l2.7 3.7M10 13.5l-2.7 3.7M8.7 9.6L4.4 8.2" stroke="currentColor" strokeWidth="1.4" /></svg>
          </span>
          <div>
            <h1>Mikeka</h1>
            <p>Under-slips across your betting accounts, checked around the clock</p>
          </div>
        </div>
        <div className="actions">
          <button className="on-dark ghost" onClick={() => setShowSettings(true)}>Settings</button>
          <button className="on-dark" onClick={() => setEditing('new')}>+ Register account</button>
        </div>
        <div className="chips">
          {settings && settings.excludedTeams.length > 0 && (
            <span className="chip" title={settings.excludedTeams.join(', ')}>
              {settings.excludedTeams.length} excluded team{settings.excludedTeams.length === 1 ? '' : 's'}
            </span>
          )}
          {settings && <span className="chip">Checks every {settings.checkIntervalMinutes} min · {settings.maxDaysAhead} days ahead</span>}
          {settings && <span className="chip mode">{settings.dryRun ? 'Run now: dry run' : settings.placeBets ? 'Runs place bets automatically' : 'Run now fills the bet slip · you place it'}</span>}
        </div>
      </div>
    </header>
    <div className="page">
      <section className="summary">
        <button type="button" className="stat-button" onClick={openTotalHistory} title="Show the overall balance history">
          <Stat label="Total balance" value={money(totalBalance)}
            note={<>{accounts.length} account{accounts.length === 1 ? '' : 's'} · <u>history</u></>} />
        </button>
        <Stat label="Active accounts" value={`${accounts.filter((a) => a.isActive && !a.stopped).length} / ${accounts.length}`}
          note={accounts.some((a) => a.stopped) ? 'some stopped' : 'all running'} tone={accounts.some((a) => a.stopped) ? 'bad' : undefined} />
        <Stat label="Waiting for you" value={waitingAll} note="slips to place or mark Won/Lost" tone={waitingAll > 0 ? 'warn' : undefined} />
        <Stat label="Bets placed today" value={`${new Set(placedToday.map((s) => s.accountId)).size} / ${accounts.filter((a) => a.isActive).length}`}
          note={`${placedToday.length} placed · ${todays} slips today`} tone={placedToday.length > 0 ? 'good' : undefined} />
      </section>

      {totalHistory && (
        <Modal title="Overall balance history" width={820} onClose={() => setTotalHistory(null)}
          subtitle={`All ${accounts.length} accounts added together, after every recorded balance change`
            + (currencies.length > 1 ? ` (mixed currencies: ${currencies.join(', ')})` : '')}>
          <BalanceChart points={totalHistory} currency={currencies.length === 1 ? currencies[0] : ''} />
        </Modal>
      )}

      {showSettings && <SettingsPanel onClose={() => setShowSettings(false)} onSaved={setSettings} />}

      {notice && <p className={notice.kind === 'error' ? 'error banner' : 'ok banner'}>{notice.text}</p>}

      {editing && (
        <AccountForm
          settings={settings}
          account={editing === 'new' ? undefined : editing}
          onCancel={() => setEditing(null)}
          onSaved={(a) => {
            setEditing(null)
            setNotice({ kind: 'ok', text: `Saved ${a.name}.` })
            load()
          }}
        />
      )}

      <section className="accounts">
        {accounts.length === 0 && !editing && (
          <div className="card empty">No accounts yet. Register one with its leagues to start.</div>
        )}
        {accounts.map((a) => {
          const waiting = slips.filter((s) => s.accountId === a.id && s.status === 'Pending').length
          const placed = placedToday.find((s) => s.accountId === a.id)
          return (
          <article
            key={a.id}
            className={`card account ${!a.isActive ? 'inactive' : ''}`}
            style={{ '--site': SITE_COLORS[a.site] } as React.CSSProperties}
            onClick={() => setSelected(a.id)}
          >
            <div className="account-head">
              <SiteAvatar account={a} />
              <div className="account-title">
                <h3>{a.name}</h3>
                <span className="muted small">{SITE_NAMES[a.site] ?? a.site} · {a.username}</span>
              </div>
              <div className="badges">
                {a.stopped ? <span className="badge lost">Stopped</span> : a.isActive ? <span className="badge won">● Active</span> : <span className="badge">Disabled</span>}
              </div>
            </div>
            <div className="balance-line">
              <span className="stat-label">Balance</span>
              <span className="big">{a.lastBalance == null ? '—' : a.lastBalance.toLocaleString()} <small>{a.currency}</small></span>
            </div>
            <div className="mini">
              <div><span className="stat-label">Next stake</span><b>{money(a.nextStake, a.currency)}</b></div>
              <div><span className="stat-label">Loss streak</span><StreakDots streak={a.lossStreak} max={a.maxLosses} /></div>
            </div>
            <div className="limits-line muted small">
              Picks {range(a.minPickOdds, a.maxPickOdds)} · Combined {range(a.minCombinedOdds, a.maxCombinedOdds)} · stops after {a.maxLosses} losses
              {a.isActive && !a.stopped && (
                <div>Next check: {!a.nextCheckAt || new Date(a.nextCheckAt) <= new Date() ? 'due now' : `${eat(a.nextCheckAt)} EAT`}</div>
              )}
            </div>
            {placed && (
              <div className="placed-today" title={placed.betReference ? `Bet number ${placed.betReference}` : undefined}>
                <span className="tick" aria-hidden="true">✓</span>
                <span><b>Bet placed today</b> · slip #{placed.id} · {money(placed.stake, a.currency)} @ {placed.combinedOdds.toFixed(2)}
                  {placed.status !== 'Pending' && <> · {placed.status}</>}</span>
              </div>
            )}
            {waiting > 0 && (
              <div className="waiting" title="Slips in the bet slip or placed, waiting to be marked Won or Lost">
                {waiting} slip{waiting === 1 ? '' : 's'} waiting for you · open to mark Won / Lost
              </div>
            )}
            <ol className="account-markets">
              {a.markets.map((m, i) => (
                <li key={i}>
                  <span className="tag market">{choiceText(m)}</span>
                  <span className="muted small">{m.leagues.join(' · ')}</span>
                </li>
              ))}
            </ol>
            <div className="actions card-actions" onClick={(e) => e.stopPropagation()}>
              <details className="more">
                <summary className="button ghost" aria-label="More actions">⋯</summary>
                <div className="menu" onClick={(e) => { e.currentTarget.closest('details')!.open = false }}>
                  <button className="ghost" disabled={busy === a.id}
                    onClick={() => act(a.id, () => api.check(a.id), (r) => {
                      const x = r as { ok: boolean; message: string }
                      if (!x.ok) throw new Error(`${a.name}: ${x.message}`)
                      return `${a.name}: ${x.message}`
                    })}>
                    Check balance
                  </button>
                  <button className="ghost" disabled={busy === a.id}
                    onClick={() => act(a.id, () => api.readMatches(a.id), (r) => {
                      const x = r as MatchesResult
                      if (!x.ok) throw new Error(`${a.name}: ${x.message}`)
                      setPreview({ account: a, result: x })
                      return `${a.name}: ${x.message}`
                    })}>
                    Read matches
                  </button>
                  <button className="ghost" onClick={() => setAnalysing(a)}>Analyse</button>
                  <button className="ghost" onClick={() => setEditing(a)}>Edit</button>
                </div>
              </details>
              <button className="ghost" onClick={() => setSelected(a.id)}>Open</button>
              <button className="run" disabled={busy === a.id || a.stopped || !a.isActive}
                onClick={async () => {
                  const siteName = SITE_NAMES[a.site] ?? a.site
                  if (settings && !settings.dryRun && !(await confirm(settings.placeBets ? {
                    title: `Place a bet on ${siteName}?`, icon: '▶', tone: 'danger', confirmLabel: 'Place bet',
                    message: <>The system reads the open slip's result if there is one, then finds matches from now on, fills the slip, types the stake and <b>clicks Place</b>: real money is bet without another check.</>,
                    facts: [['Account', a.name], ['Stake', money(a.nextStake, a.currency)], ['Site', siteName]],
                  } : {
                    title: `Fill the ${siteName} bet slip?`, icon: '▶', confirmLabel: 'Fill bet slip',
                    message: <>The system reads the open slip's result if there is one, then finds matches from now on, clicks the odds into the slip and types the stake. It never clicks <b>Place</b>: you check the slip and place it yourself.</>,
                    facts: [['Account', a.name], ['Stake', money(a.nextStake, a.currency)], ['Site', siteName]],
                  }))) return
                  act(a.id, () => api.run(a.id), (r) => {
                    const x = r as { outcome: string; message: string }
                    if (x.outcome === 'NotPlaced' || x.outcome === 'NotFilled' || x.outcome === 'Unconfirmed' || x.outcome === 'Failed') throw new Error(`${a.name}: ${x.outcome} — ${x.message}`)
                    return `${a.name}: ${x.outcome} — ${x.message}`
                  })
                }}>
                {busy === a.id ? 'Running…' : '▶ Run now'}
              </button>
              {a.stopped && (
                <button className="warn" disabled={busy === a.id}
                  onClick={() => act(a.id, () => api.resetAccount(a.id), () => `${a.name} reset; next stake ${money(a.baseStake, a.currency)}.`)}>
                  Reset &amp; resume
                </button>
              )}
            </div>
          </article>
          )
        })}
      </section>

      {accounts.filter((a) => a.id === selected).map((a) => (
        <AccountPanel key={a.id} account={a} slips={slips.filter((s) => s.accountId === a.id)} refreshKey={refreshKey}
          onChanged={load} onClose={() => setSelected(undefined)} />
      ))}

      {analysing && <AnalysisPanel account={analysing} onClose={() => setAnalysing(null)} onDone={load} />}

      {preview && (
        <Modal title={`Matches read for ${preview.account.name}`} subtitle={preview.result.message} width={1000}
          icon={<SiteAvatar account={preview.account} size={44} />} onClose={() => setPreview(null)}>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Kickoff (EAT)</th><th>Match</th><th>Under lines / intervals read</th><th>Pick ({range(preview.account.minPickOdds, preview.account.maxPickOdds)})</th></tr></thead>
              <tbody>
                {preview.result.matches.map((m) => (
                  <tr key={m.url}>
                    <td>{eat(m.kickoff)}</td>
                    <td>{m.home} v {m.away}<div className="muted small">{m.league}</div></td>
                    <td className="small">{m.selections.filter((x) => x.side === 'Under').length === 0 ? <span className="muted">none</span> :
                      m.selections.filter((x) => x.side === 'Under').map((x, i) => <div key={i}>{pickText(x)} @ {x.odds.toFixed(2)}</div>)}</td>
                    <td>{m.pick ? <b>{pickText(m.pick)} @ {m.pick.odds.toFixed(2)}</b> : <span className="muted">no line in range</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Modal>
      )}
    </div>
    </>
  )
}
