import { useEffect, useState, type FormEvent } from 'react'
import { api, type Settings } from './api'
import { Modal } from './ui'

/** System-wide odds settings, stored in the database. */
type OddsKey = 'minPickOdds' | 'maxPickOdds' | 'minCombinedOdds' | 'maxCombinedOdds'

export function SettingsPanel({ onClose, onSaved }: { onClose: () => void; onSaved: (s: Settings) => void }) {
  const [form, setForm] = useState<Settings | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    api.settings().then(setForm).catch((e) => setError((e as Error).message))
  }, [])

  const subtitle = 'Apply to all accounts from the next analysis or run'
  if (!form) return (
    <Modal title="Settings" subtitle={subtitle} width={600} onClose={onClose}>
      {error ? <p className="error">{error}</p> : <p className="muted">Loading settings…</p>}
    </Modal>
  )

  const set = (k: OddsKey, v: string) => { setSaved(false); setForm({ ...form, [k]: v === '' ? 0 : +v }) }
  // What the most picks allowed at the top odds can reach, to warn before saving.
  const best = Math.pow(form.maxPickOdds || 0, form.maxMatches)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const s = await api.saveSettings(form)
      setForm(s)
      setSaved(true)
      onSaved(s)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const odds = (k: OddsKey, label: string) => (
    <label>
      {label}
      <input type="number" step="0.01" min="1.01" value={form[k] || ''} onChange={(e) => set(k, e.target.value)} required />
    </label>
  )

  return (
    <Modal title="Settings" subtitle={subtitle} width={600} dismissable={false} onClose={onClose}
      footer={<>
        {error && <p className="error foot-error">{error}</p>}
        {saved && <span className="pos foot-error">Saved.</span>}
        <button type="button" className="ghost" onClick={onClose}>Close</button>
        <button type="submit" form="settings-form" disabled={saving}>{saving ? 'Saving…' : 'Save settings'}</button>
      </>}>
    <form id="settings-form" className="form settings" onSubmit={submit}>

      <fieldset>
        <legend>Odds per pick</legend>
        <div className="row2">
          {odds('minPickOdds', 'Minimum')}
          {odds('maxPickOdds', 'Maximum')}
        </div>
        <p className="muted small">A selection is only used if its odds are within this range; otherwise the next market is tried.</p>
      </fieldset>

      <fieldset>
        <legend>Combined odds of the slip</legend>
        <div className="row2">
          {odds('minCombinedOdds', 'Minimum')}
          {odds('maxCombinedOdds', 'Maximum')}
        </div>
        <p className="muted small">
          Up to {form.maxMatches} picks. With picks up to {(form.maxPickOdds || 0).toFixed(2)}, the highest possible slip is {best.toFixed(2)}
          {best < form.minCombinedOdds && <b className="neg"> — below the minimum, so no slip could ever be built.</b>}
        </p>
      </fieldset>

      <p className="muted small">Last changed {new Date(form.updatedAt).toLocaleString('en-GB', { timeZone: 'Africa/Dar_es_Salaam' })} EAT.</p>
    </form>
    </Modal>
  )
}
