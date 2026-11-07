import { msg, type Message } from '../../../shared/i18n/message.ts'

export interface TierRow {
  threshold: string
  price: string
}

export interface PlanValues {
  name: string
  discount: string
  effectiveFrom: string
  tiers: TierRow[]
}

export const NAME_MAX_LENGTH = 100

// Client-side checks before sending; the server stays authoritative. Keys match the server's error keys.
export function validatePlan(values: PlanValues, today: string): Record<string, Message> {
  const errors: Record<string, Message> = {}

  if (values.name.trim() === '') errors.name = msg('planForm.validation.nameRequired')
  else if (values.name.length > NAME_MAX_LENGTH) errors.name = msg('planForm.validation.nameMax', { max: NAME_MAX_LENGTH })

  const discount = Number(values.discount)
  if (values.discount.trim() === '' || Number.isNaN(discount) || discount < 0 || discount > 100) {
    errors.discount = msg('planForm.validation.discountRange')
  }

  if (values.effectiveFrom === '') errors.effectiveFrom = msg('planForm.validation.dateRequired')
  else if (values.effectiveFrom < today) errors.effectiveFrom = msg('planForm.validation.datePast')

  if (values.tiers.length === 0) errors.tiers = msg('planForm.validation.tiersRequired')

  // The last tier has no threshold (it is never sent), so only the others are checked.
  let previous = 0
  values.tiers.forEach((row, i) => {
    if (i < values.tiers.length - 1) {
      const threshold = Number(row.threshold)
      if (row.threshold.trim() === '' || Number.isNaN(threshold)) errors[`tiers[${i}].threshold`] = msg('planForm.validation.thresholdRequired')
      else if (threshold <= 0) errors[`tiers[${i}].threshold`] = msg('planForm.validation.thresholdPositive')
      else if (threshold <= previous) errors[`tiers[${i}].threshold`] = msg('planForm.validation.thresholdIncreasing')
      else previous = threshold
    }

    const price = Number(row.price)
    if (row.price.trim() === '' || Number.isNaN(price)) errors[`tiers[${i}].pricePerKwh`] = msg('planForm.validation.priceRequired')
    else if (price < 0) errors[`tiers[${i}].pricePerKwh`] = msg('planForm.validation.priceMin')
  })

  return errors
}
