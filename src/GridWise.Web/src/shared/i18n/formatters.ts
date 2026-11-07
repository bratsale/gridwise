export type Language = 'en' | 'sr'

export const LOCALES: Record<Language, string> = { en: 'en', sr: 'sr-Latn' }

type Value = number | string

// Display only: the API sends the amounts, nothing is calculated here.
export function createFormatters(language: Language) {
  const locale = LOCALES[language]
  const money = new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  const quantity = new Intl.NumberFormat(locale, { maximumFractionDigits: 3 })
  const price = new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 4 })
  const percent = new Intl.NumberFormat(locale, { maximumFractionDigits: 2 })
  const date = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' })
  const dateTime = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' })

  return {
    money: (value: Value) => money.format(Number(value)),
    kwh: (value: Value) => `${quantity.format(Number(value))} kWh`,
    price: (value: Value) => price.format(Number(value)),
    // The API uses a fraction (0.05), the screens show a percentage.
    percent: (fraction: Value) => `${percent.format(Number((Number(fraction) * 100).toFixed(2)))} %`,
    // Plan dates are calendar dates (YYYY-MM-DD), shown as the same day in UTC.
    date: (isoDate: string) => {
      const parsed = new Date(`${isoDate}T00:00:00Z`)
      return Number.isNaN(parsed.getTime()) ? isoDate : date.format(parsed)
    },
    dateTime: (isoDateTime: string) => dateTime.format(new Date(isoDateTime)),
  }
}

export type Formatters = ReturnType<typeof createFormatters>
