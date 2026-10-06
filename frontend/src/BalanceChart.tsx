import { useMemo, useState } from 'react'
import type { BalancePoint } from './api'

const W = 720, H = 220, PAD = { top: 12, right: 16, bottom: 28, left: 72 }
const eat = (iso: string) =>
  new Date(iso).toLocaleString('en-GB', { timeZone: 'Africa/Dar_es_Salaam', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
const day = (t: number) => new Date(t).toLocaleDateString('en-GB', { timeZone: 'Africa/Dar_es_Salaam', day: '2-digit', month: 'short' })

/** "Nice" round steps for the value axis. */
function ticks(min: number, max: number, count = 4) {
  const span = max - min || Math.abs(max) || 1
  const raw = span / count
  const mag = 10 ** Math.floor(Math.log10(raw))
  const step = [1, 2, 2.5, 5, 10].map((m) => m * mag).find((s) => s >= raw) ?? raw
  const lo = Math.floor(min / step) * step, hi = Math.ceil(max / step) * step
  const out: number[] = []
  for (let v = lo; v <= hi + step / 2; v += step) out.push(v)
  return out
}

/** One account's balance over time: a single line, hover shows the exact value, a table below lists every change. */
export function BalanceChart({ points, currency }: { points: BalancePoint[]; currency: string }) {
  const [hover, setHover] = useState<number | null>(null)
  const data = useMemo(() => points.map((p) => ({ t: new Date(p.at).getTime(), v: p.balance, at: p.at })), [points])

  if (data.length === 0) return <p className="muted">No balance recorded yet. It is recorded each time a run or "Check balance" reads it.</p>

  const t0 = data[0].t, t1 = data[data.length - 1].t === t0 ? t0 + 3_600_000 : data[data.length - 1].t
  const yt = ticks(Math.min(...data.map((d) => d.v)), Math.max(...data.map((d) => d.v)))
  const y0 = yt[0], y1 = yt[yt.length - 1] === y0 ? y0 + 1 : yt[yt.length - 1]
  const x = (t: number) => PAD.left + ((t - t0) / (t1 - t0)) * (W - PAD.left - PAD.right)
  const y = (v: number) => PAD.top + (1 - (v - y0) / (y1 - y0)) * (H - PAD.top - PAD.bottom)
  // Balance holds until the next reading: draw steps, not slopes.
  const path = data.map((d, i) => (i === 0 ? `M${x(d.t)},${y(d.v)}` : `H${x(d.t)}V${y(d.v)}`)).join('')
  const xt = data.length === 1 ? [t0] : [t0, t0 + (t1 - t0) / 2, t1]
  const h = hover == null ? null : data[hover]

  const onMove = (e: React.MouseEvent<SVGRectElement>) => {
    const box = e.currentTarget.getBoundingClientRect()
    const t = t0 + ((e.clientX - box.left) / box.width) * (t1 - t0)
    let best = 0
    data.forEach((d, i) => { if (Math.abs(d.t - t) < Math.abs(data[best].t - t)) best = i })
    setHover(best)
  }

  return (
    <div className="balance">
      <div className="chart-wrap">
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`Balance over time, from ${data[0].v.toLocaleString()} to ${data[data.length - 1].v.toLocaleString()} ${currency}`}>
          {yt.map((v) => (
            <g key={v}>
              <line className="grid" x1={PAD.left} x2={W - PAD.right} y1={y(v)} y2={y(v)} />
              <text className="axis" x={PAD.left - 8} y={y(v)} textAnchor="end" dominantBaseline="middle">{v.toLocaleString()}</text>
            </g>
          ))}
          {xt.map((t, i) => (
            <text key={i} className="axis" x={x(t)} y={H - 8} textAnchor={i === 0 ? 'start' : i === xt.length - 1 ? 'end' : 'middle'}>
              {t1 - t0 < 3 * 86_400_000 ? eat(new Date(t).toISOString()) : day(t)}
            </text>
          ))}
          <path className="series" d={path} />
          {data.length === 1 && <circle className="dot" cx={x(data[0].t)} cy={y(data[0].v)} r={4} />}
          {h && (
            <g>
              <line className="crosshair" x1={x(h.t)} x2={x(h.t)} y1={PAD.top} y2={H - PAD.bottom} />
              <circle className="dot" cx={x(h.t)} cy={y(h.v)} r={5} />
            </g>
          )}
          <rect x={PAD.left} y={PAD.top} width={W - PAD.left - PAD.right} height={H - PAD.top - PAD.bottom}
            fill="transparent" onMouseMove={onMove} onMouseLeave={() => setHover(null)} />
        </svg>
        {h && (
          <div className="tooltip" style={{ left: `${(x(h.t) / W) * 100}%` }}>
            <b>{h.v.toLocaleString()} {currency}</b>
            <div className="muted small">{eat(h.at)}</div>
          </div>
        )}
      </div>
      <div className="table-wrap balance-table">
        <table>
          <thead><tr><th>When (EAT)</th><th className="num">Balance</th><th className="num">Change</th></tr></thead>
          <tbody>
            {[...data].reverse().map((d, i, arr) => {
              const prev = arr[i + 1]
              const diff = prev ? d.v - prev.v : null
              return (
                <tr key={d.at + i}>
                  <td>{eat(d.at)}</td>
                  <td className="num">{d.v.toLocaleString()} {currency}</td>
                  <td className={`num ${diff != null && diff < 0 ? 'neg' : diff ? 'pos' : ''}`}>
                    {diff == null ? '—' : `${diff > 0 ? '+' : ''}${diff.toLocaleString()}`}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}
