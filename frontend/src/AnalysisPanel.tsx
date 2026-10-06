import { useEffect, useState } from 'react'
import { api, type Account, type AnalysisReport } from './api'
import { Modal, SITE_COLORS, SiteAvatar } from './ui'

const eat = (iso: string) =>
  new Date(iso).toLocaleString('en-GB', { timeZone: 'Africa/Dar_es_Salaam', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
const nice = (local: string) => local.replace('T', ' ')

/** Read-only analysis of a betting window with the account's settings. Never places a bet. */
export function AnalysisPanel({ account, onClose, onDone }: { account: Account; onClose: () => void; onDone: () => void }) {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [running, setRunning] = useState(false)
  const [report, setReport] = useState<AnalysisReport | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  const createSlip = async () => {
    setCreating(true)
    setError(null)
    try {
      const s = await api.createSlip(account.id)
      setCreated(`Draft slip #${s.id} saved: ${s.picks} picks at ${s.combinedOdds.toFixed(2)}, stake ${s.stake.toLocaleString()} ${account.currency}. It is in the Slips table; not placed yet.`)
      onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setCreating(false)
    }
  }

  useEffect(() => {
    api.analysisWindow(account.id).then((w) => { setFrom(w.from); setTo(w.to) }).catch((e) => setError((e as Error).message))
  }, [account.id])

  const run = async () => {
    setRunning(true)
    setError(null)
    setReport(null)
    setCreated(null)
    try {
      const r = await api.analyse(account.id, from, to)
      if ('error' in r) setError(r.error)
      else setReport(r)
      onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setRunning(false)
    }
  }

  const cur = account.currency
  return (
    <Modal title={`Analyse ${account.name}`} subtitle="Public site, no login, no bet is placed" width={1000}
      icon={<SiteAvatar account={account} size={44} />} onClose={onClose}
      style={{ '--site': SITE_COLORS[account.site] } as React.CSSProperties}>
    <div className="analysis">
      <div className="window-row">
        <label>From (EAT)<input type="datetime-local" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To (EAT)<input type="datetime-local" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <button disabled={running || !from || !to} onClick={run}>{running ? 'Analysing… (watch the browser window)' : 'Run analysis'}</button>
      </div>
      {error && <p className="error">{error}</p>}

      {report && (
        <>
          <div className={`verdict ${report.canCreateSlip ? 'yes' : 'no'}`}>
            <b>{report.canCreateSlip ? 'Slip ready' : 'No slip'}</b> — {report.verdict}
            {report.canCreateSlip && !created && (
              <button className="create-slip" disabled={creating} onClick={createSlip}>{creating ? 'Saving…' : 'Create slip'}</button>
            )}
          </div>
          {created && <p className="ok banner">{created}</p>}
          <dl className="analysis-summary">
            <div><dt>Window</dt><dd>{nice(report.fromEat.slice(0, 16))} → {nice(report.toEat.slice(0, 16))}</dd></div>
            <div><dt>Last known balance</dt><dd>{report.lastKnownBalance != null ? `${report.lastKnownBalance.toLocaleString()} ${cur}` : '—'}<div className="muted small">checked when placing</div></dd></div>
            <div><dt>Stake</dt><dd>{report.stake.toLocaleString()} {cur}<div className="muted small">loss streak {report.lossStreak}</div></dd></div>
            <div><dt>Combined odds</dt><dd>{report.combinedOdds?.toFixed(2) ?? '—'}</dd></div>
            <div><dt>Potential return</dt><dd>{report.potentialReturn != null ? `${report.potentialReturn.toLocaleString()} ${cur}` : '—'}</dd></div>
            <div><dt>Rough chance</dt><dd>{report.chancePercent != null ? `≈ ${report.chancePercent}%` : '—'}<div className="muted small">from the bookmaker's odds</div></dd></div>
          </dl>
          <div className="table-wrap">
            <table>
              <thead><tr><th>Kickoff (EAT)</th><th>Match</th><th>Decision and reasons</th><th>Pick</th></tr></thead>
              <tbody>
                {report.matches.length === 0 && <tr><td colSpan={4} className="muted">No matches found in this window.</td></tr>}
                {report.matches.map((m) => (
                  <tr key={m.url} className={m.inSlip ? 'in-slip' : ''}>
                    <td>{eat(m.kickoff)}</td>
                    <td>{m.home} v {m.away}<div className="muted small">{m.league}</div></td>
                    <td className="small">{m.steps.map((s, i) => <div key={i}>{s}</div>)}</td>
                    <td>
                      {m.pick ? <>{m.pick} @ {m.pickOdds?.toFixed(2)}</> : <span className="muted">no bet</span>}
                      {m.inSlip && <div className="badge won">in slip</div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
    </Modal>
  )
}
