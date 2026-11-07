const money = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const quantity = new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 })
const price = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })

type Value = number | string

// Display only: the API sends the amounts, nothing is calculated here.
export const formatMoney = (value: Value) => money.format(Number(value))
export const formatKwh = (value: Value) => `${quantity.format(Number(value))} kWh`
export const formatPrice = (value: Value) => price.format(Number(value))

// The API uses a fraction (0.05), the screens use a percentage (5 = 5%).
export const percentToFraction = (percent: number) => Number((percent / 100).toFixed(4))
export const fractionToPercent = (fraction: Value) => Number((Number(fraction) * 100).toFixed(2))
export const formatPercent = (fraction: Value) => `${fractionToPercent(fraction)} %`

export const todayUtc = () => new Date().toISOString().slice(0, 10)
