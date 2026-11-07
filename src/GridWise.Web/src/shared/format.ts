// The API uses a fraction (0.05), the screens use a percentage (5 = 5%).
export const percentToFraction = (percent: number) => Number((percent / 100).toFixed(4))
export const fractionToPercent = (fraction: number | string) => Number((Number(fraction) * 100).toFixed(2))

export const todayUtc = () => new Date().toISOString().slice(0, 10)
