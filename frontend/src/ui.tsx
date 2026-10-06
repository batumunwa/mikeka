import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import type { Account, Site } from './api'

/** Each betting site's own colour (cards, avatars). Text never uses it: it marks identity only. */
export const SITE_COLORS: Record<Site, string> = { coldbet: '#0891b2', '1win': '#4f46e5', leonbet: '#ea580c' }

export const money = (n: number | null | undefined, cur = 'TZS') => (n == null ? '—' : `${n.toLocaleString()} ${cur}`)

/** Round badge with the site's initial in the site colour. */
export function SiteAvatar({ account, size = 40 }: { account: Account; size?: number }) {
  const c = SITE_COLORS[account.site] ?? '#64748b'
  return (
    <span className="avatar" style={{ width: size, height: size, background: `${c}1f`, color: c, borderColor: `${c}55`, fontSize: size * 0.42 }} aria-hidden>
      {account.site === '1win' ? '1W' : account.site[0].toUpperCase()}
    </span>
  )
}

/** Loss streak as dots: filled ones are losses in a row; betting stops when all are filled. */
export function StreakDots({ streak, max = 4 }: { streak: number; max?: number }) {
  return (
    <span className="streak" aria-label={`${streak} of ${max} losses in a row`}>
      {Array.from({ length: max }, (_, i) => <i key={i} className={i < streak ? (streak >= max - 1 ? 'hot' : 'on') : ''} />)}
      <b>{streak}/{max}</b>
    </span>
  )
}

/** A small figure tile: label, big value, optional note. */
export function Stat({ label, value, note, tone }: { label: string; value: React.ReactNode; note?: React.ReactNode; tone?: 'warn' | 'good' | 'bad' }) {
  return (
    <div className={`stat ${tone ?? ''}`}>
      <span className="stat-label">{label}</span>
      <span className="stat-value">{value}</span>
      {note && <span className="stat-note">{note}</span>}
    </div>
  )
}

/**
 * A pop-up window over the page (modal <dialog>): fixed title bar, scrolling body, optional fixed footer for buttons.
 * Esc, ✕ and a click on the dimmed area close it; with dismissable=false (forms with unsaved typing) only ✕ and the
 * footer buttons do.
 */
export function Modal({ title, subtitle, icon, width = 720, dismissable = true, footer, style, onClose, children }: {
  title: ReactNode; subtitle?: ReactNode; icon?: ReactNode; width?: number; dismissable?: boolean
  footer?: ReactNode; style?: React.CSSProperties; onClose: () => void; children: ReactNode
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  // No close() on cleanup: removing the element ends the modal, and a close() would fire onClose
  // (React runs this effect twice in development).
  useEffect(() => {
    const d = dialog.current
    if (d && !d.open) d.showModal()
  }, [])
  return (
    <dialog ref={dialog} className="modal" style={{ ...style, width: `min(${width}px, calc(100vw - 32px))` }}
      onCancel={(e) => { if (!dismissable) e.preventDefault() }}
      onClose={onClose}
      onClick={(e) => { if (dismissable && e.target === e.currentTarget) onClose() }}>
      <div className="modal-box">
        <div className="modal-head">
          {icon}
          <div className="modal-title">
            <h2>{title}</h2>
            {subtitle && <span className="muted small">{subtitle}</span>}
          </div>
          <button type="button" className="ghost close" onClick={onClose} aria-label="Close">✕</button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-foot">{footer}</div>}
      </div>
    </dialog>
  )
}

export type ConfirmOptions = {
  title: ReactNode
  /** One or two plain sentences: what happens, and when to do it. */
  message?: ReactNode
  /** Short label/value rows (slip, stake, …) so the choice is made with the facts in view. */
  facts?: [string, ReactNode][]
  confirmLabel: string
  tone?: 'primary' | 'won' | 'lost' | 'danger'
  icon?: string
}

/**
 * The app's own confirmation window (instead of the browser's "localhost says" box).
 * `const [confirm, confirmDialog] = useConfirm()`; render `{confirmDialog}`; `if (!(await confirm({...}))) return`.
 */
export function useConfirm() {
  const [req, setReq] = useState<(ConfirmOptions & { resolve: (ok: boolean) => void }) | null>(null)
  const ask = useCallback((o: ConfirmOptions) => new Promise<boolean>((resolve) => setReq({ ...o, resolve })), [])
  const answer = (ok: boolean) => {
    req?.resolve(ok)
    setReq(null)
  }
  const tone = req?.tone ?? 'primary'
  const dialog = req && (
    <Modal width={460} title={req.title} onClose={() => answer(false)}
      icon={<span className={`confirm-icon ${tone}`} aria-hidden="true">{req.icon ?? '?'}</span>}
      footer={<>
        <button type="button" className="ghost" onClick={() => answer(false)}>Cancel</button>
        <button type="button" className={`confirm-btn ${tone}`} autoFocus onClick={() => answer(true)}>{req.confirmLabel}</button>
      </>}>
      <div className="confirm">
        {req.message && <p>{req.message}</p>}
        {req.facts && req.facts.length > 0 && (
          <dl className="confirm-facts">
            {req.facts.map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}
          </dl>
        )}
      </div>
    </Modal>
  )
  return [ask, dialog] as const
}
