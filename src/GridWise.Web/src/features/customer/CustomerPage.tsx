
import { Banner } from '../../shared/ui/Banner.tsx'
import { CustomerForm } from './components/CustomerForm.tsx'
import { RecommendationResult } from './components/RecommendationResult.tsx'
import { useCalculator } from './hooks/useCalculator.ts'
import { useTaxGroups } from './hooks/useTaxGroups.ts'

export function CustomerPage() {
  const groups = useTaxGroups()
  const calc = useCalculator(groups.taxGroups)

  return (
    <section>
      <CustomerForm
        consumption={calc.consumption}
        taxGroup={calc.taxGroup}
        taxGroups={groups.taxGroups}
        taxGroupsLoading={groups.loading}
        submitting={calc.submitting}
        errors={calc.fieldErrors}
        onConsumption={calc.setConsumption}
        onTaxGroup={calc.setTaxGroup}
        onBlur={calc.touch}
        onSubmit={(e) => void calc.calculate(e)}
      />

      {groups.error && <Banner kind="error" message={groups.error} />}
      {calc.message && <Banner kind="error" message={calc.message} />}

      {calc.result && (
        <RecommendationResult
          key={calc.result.id}
          result={calc.result}
          expanded={calc.expanded}
          accepting={calc.accepting}
          accepted={calc.accepted}
          acceptError={calc.acceptError}
          onToggle={calc.toggle}
          onAccept={(quote) => void calc.accept(quote)}
        />
      )}
    </section>
  )
}
