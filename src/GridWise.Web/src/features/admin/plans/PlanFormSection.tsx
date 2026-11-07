import type { Optimistic } from '../../../shared/hooks/useOptimisticList.ts'
import type { Plan } from './api/plansApi.ts'
import { PlanForm } from './components/PlanForm.tsx'
import { usePlanForm, type FormSession } from './hooks/usePlanForm.ts'
import type { Notice } from './hooks/usePlans.ts'

// Connects the form state (hook) to the form (presentation). Mounted once per opening of the form.
export function PlanFormSection({
  session, optimistic, onSaved, onCancel,
}: { session: FormSession; optimistic: Optimistic<Plan>; onSaved: (notice: Notice) => void; onCancel: () => void }) {
  const f = usePlanForm(session, optimistic, onSaved)
  return (
    <PlanForm
      editing={f.editing}
      name={f.name}
      discount={f.discount}
      effectiveFrom={f.effectiveFrom}
      tiers={f.tiers}
      submitting={f.submitting}
      conflict={f.conflict}
      message={f.message}
      general={f.general}
      errors={f.fieldErrors}
      onName={f.setName}
      onDiscount={f.setDiscount}
      onEffectiveFrom={f.setEffectiveFrom}
      onTiers={f.setTiers}
      onBlur={f.touch}
      onSubmit={(e) => void f.submit(e)}
      onReload={() => void f.reload()}
      onCancel={onCancel}
    />
  )
}
