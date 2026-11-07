import { RetirePanel } from './components/RetirePanel.tsx'
import type { Plan } from './api/plansApi.ts'
import { useRetire } from './hooks/useRetire.ts'
import type { Notice } from './hooks/usePlans.ts'

// Connects the retire state (hook) to the panel (presentation). Mounted once per plan.
export function RetireSection({
  plan, onDone, onCancel,
}: { plan: Plan; onDone: (notice: Notice) => void; onCancel: () => void }) {
  const r = useRetire(plan, onDone)
  return (
    <RetirePanel
      plan={plan}
      effectiveFrom={r.effectiveFrom}
      submitting={r.submitting}
      confirming={r.confirming}
      message={r.message}
      general={r.general}
      dateErrors={r.dateErrors}
      onDate={r.setEffectiveFrom}
      onAsk={r.ask}
      onConfirm={() => void r.retire()}
      onCancelConfirm={r.cancelConfirm}
      onCancel={onCancel}
    />
  )
}
