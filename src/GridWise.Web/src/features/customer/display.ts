// Display rules of the customer page. Nothing is calculated here: the amounts come from the API.

export const TOP_PLANS = 10

// The tax group selected until the user chooses one: household (what most customers are), else the first one.
export function preferredTaxGroup(taxGroups: readonly { code: string }[]): string {
  return taxGroups.find((g) => g.code === 'household')?.code ?? taxGroups[0]?.code ?? ''
}

// The plans arrive cheapest first; the list shows the first TOP_PLANS until the user asks for all of them.
export function visiblePlans<T>(plans: readonly T[], showAll: boolean): readonly T[] {
  return showAll ? plans : plans.slice(0, TOP_PLANS)
}

// The discount is stored as a positive amount; on the bill it is shown as a deduction. 0 stays 0 (not "-0.00").
export function asDeduction(amount: number | string): number {
  const value = Number(amount)
  return value === 0 ? 0 : -value
}
