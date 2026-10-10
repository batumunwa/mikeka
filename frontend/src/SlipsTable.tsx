import { useState } from 'react'
import { api, isPlaced, pickText, type PickResult, type Slip } from './api'
import { useConfirm, type ConfirmOptions } from './ui'

const money = (n: number | null | undefined, cur: string) => (n == null ? '—' : `${n.toLocaleString()} ${cur}`)
const eat = (iso: string) =>
  new Date(iso).toLocaleString('en-GB', { timeZone: 'Africa/Dar_es_Salaam', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })

/** Placed and waiting for its result = green "placed"; filled/unconfirmed without a bet number stays amber "pending". */
const rowTone = (s: Slip) => (s.status === 'Pending' && isPlaced(s) ? 'placed' : s.status.toLowerCase())
const statusText = (s: Slip) =>
  s.status === 'Pending' ? (isPlaced(s) ? 'Placed · awaiting result' : 'Not placed yet') : s.status

/** Green when the balance went up after the slip, red when it went down. */
const balanceTone = (s: Slip) =>
  s.balanceAfter == null || s.balanceBefore == null ? '' : s.balanceAfter > s.balanceBefore ? 'pos' : s.balanceAfter < s.balanceBefore ? 'neg' : ''

/** One account's slips: drafts, placed, won/lost and skipped days. Pending slips can be marked Won/Lost by hand. */
export function SlipsTable({ slips, currency, onChanged }: { slips: Slip[]; currency: string; onChanged?: () => void }) {
  const [busy, setBusy] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirm, confirmDialog] = useConfirm()

  const act = async (id: number, question: ConfirmOptions, fn: () => Promise<void>) => {
    if (!(await confirm(question))) return
    setBusy(id)
    setError(null)
    try {
      await fn()
      onChanged?.()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(null)
    }
  }

  /** A lost slip's pick marked won/lost (or cleared) by the user: no confirmation, a second click undoes it. */
  const markPick = async (slipId: number, pickId: number, result: PickResult | null) => {
    setBusy(slipId)
    setError(null)
    try {
      await api.setPickResult(pickId, result)
      onChanged?.()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(null)
    }
  }

  /** The slip's key facts, shown in the confirmation window. */
  const facts = (s: Slip): [string, string][] => [
    ['Day', s.betDay],
    ['Picks', `${s.picks.length}${s.combinedOdds ? ` @ ${s.combinedOdds.toFixed(2)}` : ''}`],
    ['Stake', money(s.stake, currency)],
  ]

  if (slips.length === 0) return <p className="muted">No slips yet.</p>
  return (
    <div className="table-wrap">
      {confirmDialog}
      {error && <p className="error banner">{error}</p>}
      <table className="slips">
        <thead>
          <tr>
            <th className="num">#</th><th>Day</th><th>Matches</th><th className="num">Odds</th><th className="num">Stake</th>
            <th>Result</th><th className="num">Balance</th><th>Bet ref / note</th>
          </tr>
        </thead>
        <tbody>
          {slips.map((s) => (
            <tr key={s.id} className={`slip ${rowTone(s)}`}>
              <td className="num slip-id">{s.id}</td>
              <td><b>{s.betDay}</b>{isPlaced(s) && <div className="placed-mark">✓ Placed</div>}</td>
              <td>
                {s.picks.length === 0 ? <span className="muted">—</span> : (
                  <ul className="picks">
                    {s.picks.map((p, i) => (
                      <li key={i} className={p.result ? `pick-${p.result.toLowerCase()}` : ''}>
                        <span className="kick">{eat(p.kickoff)}</span> <b>{p.home} v {p.away}</b>
                        {p.league && <><br /><span className="pick-league">{p.league}</span></>}
                        <br /><span className="small market">{pickText(p)}</span> <span className="odd">{p.odds.toFixed(2)}</span>
                        {s.status === 'Won' && <span className="pick-mark won" title="Won">✓</span>}
                        {s.status === 'Lost' && p.id != null && (
                          <span className="pick-result" title="Which picks lost this slip? (for the statistics)">
                            <button className={`pick-btn won ${p.result === 'Won' ? 'on' : ''}`} disabled={busy === s.id}
                              onClick={() => markPick(s.id, p.id!, p.result === 'Won' ? null : 'Won')}>✓</button>
                            <button className={`pick-btn lost ${p.result === 'Lost' ? 'on' : ''}`} disabled={busy === s.id}
                              onClick={() => markPick(s.id, p.id!, p.result === 'Lost' ? null : 'Lost')}>✕</button>
                          </span>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </td>
              <td className="num">{s.combinedOdds ? <span className="total-odds">{s.combinedOdds.toFixed(2)}</span> : '—'}</td>
              <td className="num stake">{money(s.stake, currency)}</td>
              <td>
                <span className={`badge ${rowTone(s)}`}>{statusText(s)}</span>
                {s.status === 'Pending' && (
                  <div className="actions">
                    <button className="won-btn" disabled={busy === s.id}
                      onClick={() => act(s.id, {
                        title: `Mark slip #${s.id} as won?`, icon: '✓', tone: 'won', confirmLabel: 'Yes, it won', facts: facts(s),
                        message: 'Only after the bet has won on the site. The loss streak goes back to zero.',
                      }, () => api.settleSlip(s.id, true))}>Won</button>
                    <button className="lost-btn" disabled={busy === s.id}
                      onClick={() => act(s.id, {
                        title: `Mark slip #${s.id} as lost?`, icon: '✕', tone: 'lost', confirmLabel: 'Yes, it lost', facts: facts(s),
                        message: 'Only after the bet has lost on the site. The next stake doubles.',
                      }, () => api.settleSlip(s.id, false))}>Lost</button>
                    {!s.betReference && (
                      <button className="ghost" disabled={busy === s.id}
                        onClick={() => act(s.id, {
                          title: `Delete slip #${s.id}?`, icon: '!', tone: 'danger', confirmLabel: 'Delete slip', facts: facts(s),
                          message: <>Only if <b>My bets</b> on the site shows this slip was <b>not placed</b>. It is removed from the system.</>,
                        }, () => api.deleteSlip(s.id))}>Not placed</button>
                    )}
                  </div>
                )}
              </td>
              <td className={`num ${balanceTone(s)}`}>{money(s.balanceAfter ?? s.balanceBefore, currency)}</td>
              <td className="small">{s.betReference ?? ''}{s.note && <div className="slip-note">{s.note}</div>}
                {s.status === 'Pending' && s.settlementChecks.length > 0 && (
                  <div className="slip-note" title="When the system reads this slip's result: a loss seen early starts the next slip">
                    Result checks: {s.settlementChecks.map((t) => {
                      const done = s.resultCheckedAt != null && new Date(s.resultCheckedAt) >= new Date(t)
                      return <span key={t} className={done ? 'muted' : ''}>{eat(t)}{done ? ' ✓' : ''} </span>
                    })}
                  </div>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
