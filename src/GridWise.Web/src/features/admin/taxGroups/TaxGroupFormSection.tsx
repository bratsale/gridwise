import type { Optimistic } from '../../../shared/hooks/useOptimisticList.ts'
import type { TaxGroup } from './api/taxGroupsApi.ts'
import { TaxGroupForm } from './components/TaxGroupForm.tsx'
import { useTaxGroupForm, type TaxGroupFormSession } from './hooks/useTaxGroupForm.ts'
import type { TaxGroupNotice } from './hooks/useTaxGroupList.ts'

// Connects the form state (hook) to the form (presentation). Mounted once per opening of the form.
export function TaxGroupFormSection({
  session, optimistic, onSaved, onCancel,
}: { session: TaxGroupFormSession; optimistic: Optimistic<TaxGroup>; onSaved: (notice: TaxGroupNotice) => void; onCancel: () => void }) {
  const f = useTaxGroupForm(session, optimistic, onSaved)
  return (
    <TaxGroupForm
      editing={f.editing}
      code={f.code}
      vat={f.vat}
      ecoTax={f.ecoTax}
      effectiveFrom={f.effectiveFrom}
      submitting={f.submitting}
      conflict={f.conflict}
      message={f.message}
      general={f.general}
      errors={f.fieldErrors}
      onCode={f.setCode}
      onVat={f.setVat}
      onEcoTax={f.setEcoTax}
      onEffectiveFrom={f.setEffectiveFrom}
      onBlur={f.touch}
      onSubmit={(e) => void f.submit(e)}
      onReload={() => void f.reload()}
      onCancel={onCancel}
    />
  )
}
