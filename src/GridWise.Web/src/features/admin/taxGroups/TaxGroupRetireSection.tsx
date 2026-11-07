import type { Optimistic } from '../../../shared/hooks/useOptimisticList.ts'
import { RetirePanel } from '../../../shared/ui/RetirePanel.tsx'
import type { TaxGroup } from './api/taxGroupsApi.ts'
import { useTaxGroupRetire } from './hooks/useTaxGroupRetire.ts'
import type { TaxGroupNotice } from './hooks/useTaxGroupList.ts'

// Connects the retire state (hook) to the panel (presentation). Mounted once per tax group.
export function TaxGroupRetireSection({
  taxGroup, optimistic, onDone, onCancel,
}: { taxGroup: TaxGroup; optimistic: Optimistic<TaxGroup>; onDone: (notice: TaxGroupNotice) => void; onCancel: () => void }) {
  const r = useTaxGroupRetire(taxGroup, optimistic, onDone)
  return (
    <RetirePanel
      name={taxGroup.code}
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
