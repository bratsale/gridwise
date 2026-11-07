import type { Optimistic } from '../../../../shared/hooks/useOptimisticList.ts'
import { useRetireFlow } from '../../../../shared/hooks/useRetireFlow.ts'
import { msg } from '../../../../shared/i18n/message.ts'
import { getTaxGroup, retireTaxGroup, type TaxGroup } from '../api/taxGroupsApi.ts'
import type { TaxGroupNotice } from './useTaxGroupList.ts'

export function useTaxGroupRetire(taxGroup: TaxGroup, optimistic: Optimistic<TaxGroup>, onDone: (notice: TaxGroupNotice) => void) {
  return useRetireFlow<TaxGroup>({
    load: () => getTaxGroup(taxGroup.code),
    retire: (effectiveFrom, etag) => retireTaxGroup(taxGroup.code, effectiveFrom, etag),
    expected: (effectiveFrom) => ({ ...taxGroup, versionNo: Number(taxGroup.versionNo) + 1, status: 'Retired', effectiveFrom }),
    optimistic,
    loadError: msg('taxGroups.oneLoadError'),
    conflict: msg('taxGroups.conflict'),
    onDone: (date) => onDone({ kind: 'retired', date }),
  })
}
