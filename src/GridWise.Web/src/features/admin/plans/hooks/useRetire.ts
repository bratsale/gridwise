import { useRetireFlow } from '../../../../shared/hooks/useRetireFlow.ts'
import type { Optimistic } from '../../../../shared/hooks/useOptimisticList.ts'
import { msg } from '../../../../shared/i18n/message.ts'
import { getPlan, retirePlan, type Plan } from '../api/plansApi.ts'
import type { Notice } from './usePlans.ts'

export function useRetire(plan: Plan, optimistic: Optimistic<Plan>, onDone: (notice: Notice) => void) {
  return useRetireFlow<Plan>({
    load: () => getPlan(plan.id),
    retire: (effectiveFrom, etag) => retirePlan(plan.id, effectiveFrom, etag),
    expected: (effectiveFrom) => ({ ...plan, versionNo: Number(plan.versionNo) + 1, status: 'Retired', effectiveFrom, tiers: [] }),
    optimistic,
    loadError: msg('plans.planLoadError'),
    conflict: msg('plans.conflict'),
    onDone: (date) => onDone({ kind: 'retired', date }),
  })
}
