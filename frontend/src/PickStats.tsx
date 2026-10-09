import { useEffect, useState } from 'react'
import { api, MARKET_NAMES, SITE_NAMES, type MarketKey, type PickStat, type Site } from './api'
import { Modal } from './ui'

/** "Goals / result · 1–10 min" or "Corners · whole match". Goals Under 0.5 and the draw in the same minutes count as one. */
const marketText = (s: PickStat) => {
  const name = s.market === 'goal' ? 'No goal' : MARKET_NAMES[s.market as MarketKey] ?? s.market
  return `${name} · ${s.range === 'match' ? 'whole match' : `${s.range.replace('-', '–')} min`}`
}

/**
 * Pick results per site, league and market, most losses first: shows which leagues and markets lose slips.
 * Won slips count every pick as won; on lost slips the user marks the losing pick(s) in the Slips table.
 */
export function PickStats({ onClose }: { onClose: () => void }) {
  const [rows, setRows] = useState<PickStat[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { api.pickStats().then(setRows, (e) => setError((e as Error).message)) }, [])

  const unmarked = rows?.reduce((n, r) => n + r.unmarked, 0) ?? 0
  return (
    <Modal title="Pick statistics" width={860} onClose={onClose}
      subtitle="Each pick of a settled slip, by site, league and market. Mark the losing picks of lost slips (✓ / ✕ in an account's Slips) to fill this in.">
      {error && <p className="error banner">{error}</p>}
      {!rows && !error && <p className="muted">Loading…</p>}
      {rows && rows.length === 0 && <p className="muted">No settled slips yet.</p>}
      {rows && rows.length > 0 && (
        <>
          {unmarked > 0 && <p className="muted small">{unmarked} pick{unmarked === 1 ? '' : 's'} of lost slips not marked yet.</p>}
          <div className="table-wrap">
            <table className="pick-stats">
              <thead>
                <tr><th>Site</th><th>League</th><th>Market</th><th className="num">Won</th><th className="num">Lost</th>
                  <th className="num">Not marked</th><th className="num">Win rate</th></tr>
              </thead>
              <tbody>
                {rows.map((r) => {
                  const known = r.won + r.lost
                  const rate = known === 0 ? null : (100 * r.won) / known
                  return (
                    <tr key={`${r.site}|${r.league}|${r.market}|${r.range}`}>
                      <td>{SITE_NAMES[r.site as Site] ?? r.site}</td>
                      <td>{r.league}</td>
                      <td className="small">{marketText(r)}</td>
                      <td className="num pos">{r.won}</td>
                      <td className={`num ${r.lost > 0 ? 'neg' : ''}`}>{r.lost}</td>
                      <td className="num muted">{r.unmarked || ''}</td>
                      <td className="num">{rate == null ? '—' : <b className={rate < 90 ? 'neg' : ''}>{rate.toFixed(0)}%</b>}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Modal>
  )
}
