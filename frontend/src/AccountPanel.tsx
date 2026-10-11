import { useCallback, useEffect, useState } from 'react'
import { api, SITE_NAMES, type Account, type BalancePoint, type RunLog, type Slip } from './api'
import { BalanceChart } from './BalanceChart'
import { SlipsTable } from './SlipsTable'
import { Modal, money, SITE_COLORS, SiteAvatar, Stat, StreakDots } from './ui'

type Tab = 'activity' | 'slips' | 'balance'

const time = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-GB', { timeZone: 'Africa/Dar_es_Salaam', hour: '2-digit', minute: '2-digit' })
const dayKey = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { timeZone: 'Africa/Dar_es_Salaam', weekday: 'long', day: 'numeric', month: 'long' })

/** The readable part of a log message: its first line, without browser call logs or raw API error bodies. */
function summary(message: string) {
  let s = message.split('\n')[0].split(' Call log:')[0]
  // API errors ("Status Code: BadRequest" + a JSON body on the next lines): keep the body's own message.
  const apiMessage = /^Status Code: /.test(s) ? message.match(/"message"\s*:\s*"([^"]+)"/)?.[1] : undefined
  if (apiMessage) s = `${s.replace(/^Status Code: /, '')}: ${apiMessage}`
  return s.length > 180 ? s.slice(0, 177) + '…' : s
}

/** What kind of event a log line is, for its dot colour: done (slip made/filled), skipped (no bet), error, or plain info. */
function kind(l: RunLog): 'error' | 'done' | 'skip' | 'info' {
  if (l.level === 'Error') return 'error'
  if (/^(Filled|Placed|Draft slip|Slip #\d+ Won)|Slip ready|Connection check OK|Balance checked/.test(l.message)) return 'done'
  if (/^(Skipped|NotSupported|NotFilled|NotPlaced|Waiting|Stopped|Paused)|Lost\./.test(l.message)) return 'skip'
  return 'info'
}

/** One account's own window (a pop-up over the page): its activity, its slips and its balance over time. */
export function AccountPanel({ account, slips, refreshKey, onChanged, onClose }:
  { account: Account; slips: Slip[]; refreshKey: number; onChanged: () => void; onClose: () => void }) {
  const [tab, setTab] = useState<Tab>('activity')
  const [logs, setLogs] = useState<RunLog[]>([])
  const [balance, setBalance] = useState<BalancePoint[]>([])
  const [errorsOnly, setErrorsOnly] = useState(false)
  const [open, setOpen] = useState<number | null>(null)

  const load = useCallback(async () => {
    try {
      const [l, b] = await Promise.all([api.logs(account.id), api.balanceHistory(account.id)])
      setLogs(l)
      setBalance(b)
    } catch { /* the page shows the API error banner */ }
  }, [account.id])

  useEffect(() => { load() }, [load, refreshKey])

  const shown = errorsOnly ? logs.filter((l) => l.level === 'Error') : logs
  const waiting = slips.filter((s) => s.status === 'Pending').length
  const settled = slips.filter((s) => s.status === 'Won' || s.status === 'Lost')
  const won = settled.filter((s) => s.status === 'Won').length

  // Activity grouped by day, newest first.
  const days: { day: string; items: RunLog[] }[] = []
  for (const l of shown) {
    const d = dayKey(l.at)
    if (days.length === 0 || days[days.length - 1].day !== d) days.push({ day: d, items: [] })
    days[days.length - 1].items.push(l)
  }

  return (
    <Modal width={1000} title={account.name} subtitle={`${SITE_NAMES[account.site] ?? account.site} · ${account.username}`}
      icon={<SiteAvatar account={account} size={44} />} onClose={onClose}
      style={{ '--site': SITE_COLORS[account.site] } as React.CSSProperties}>
    <div className="account-panel">

      <div className="summary in-panel">
        <Stat label="Balance" value={money(account.lastBalance, account.currency)} />
        <Stat label="Next stake" value={money(account.nextStake, account.currency)} note={`base ${account.baseStake.toLocaleString()}`} />
        <Stat label="Loss streak" value={<StreakDots streak={account.lossStreak} max={account.maxLosses} />} tone={account.lossStreak >= account.maxLosses - 1 && account.lossStreak > 0 ? 'bad' : undefined} />
        <Stat label="Results" value={settled.length ? `${won} won / ${settled.length - won} lost` : 'none yet'}
          note={waiting ? `${waiting} waiting for you` : undefined} tone={waiting ? 'warn' : undefined} />
      </div>

      <div className="segmented" role="tablist">
        <button role="tab" aria-selected={tab === 'activity'} className={tab === 'activity' ? 'on' : ''} onClick={() => setTab('activity')}>Activity</button>
        <button role="tab" aria-selected={tab === 'slips'} className={tab === 'slips' ? 'on' : ''} onClick={() => setTab('slips')}>
          Slips <span className="count">{slips.length}</span>{waiting > 0 && <span className="badge pending">{waiting} waiting</span>}
        </button>
        <button role="tab" aria-selected={tab === 'balance'} className={tab === 'balance' ? 'on' : ''} onClick={() => setTab('balance')}>Balance history</button>
      </div>

      {tab === 'activity' && (
        <div className="activity">
          <label className="check small"><input type="checkbox" checked={errorsOnly} onChange={(e) => setErrorsOnly(e.target.checked)} /> Errors only</label>
          {days.map((g) => (
            <div key={g.day} className="day">
              <h4>{g.day}</h4>
              <ul className="timeline">
                {g.items.map((l) => {
                  const short = summary(l.message)
                  return (
                    <li key={l.id} className={kind(l)}>
                      <span className="t">{time(l.at)}</span>
                      <span className="dot" aria-hidden />
                      <span className="msg">
                        {short}
                        {short !== l.message && (
                          <button className="link small" onClick={() => setOpen(open === l.id ? null : l.id)}>{open === l.id ? 'less' : 'details'}</button>
                        )}
                        {open === l.id && <pre className="details">{l.message}</pre>}
                      </span>
                    </li>
                  )
                })}
              </ul>
            </div>
          ))}
          {shown.length === 0 && <p className="muted">{errorsOnly ? 'No errors.' : 'Nothing yet.'}</p>}
        </div>
      )}

      {tab === 'slips' && <SlipsTable slips={slips} currency={account.currency} onChanged={onChanged} />}

      {tab === 'balance' && <BalanceChart points={balance} currency={account.currency} />}
    </div>
    </Modal>
  )
}
