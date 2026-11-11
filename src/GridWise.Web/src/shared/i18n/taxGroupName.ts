import { en, type TranslationKey } from './en.ts'

// The name of a tax group in the current language, next to its code: "Domaćinstvo (household)". The code stays visible, because
// it is what the API, the CSV files and the admin use. A code without a name in the dictionaries is shown as it is.
export function taxGroupLabel(t: (key: TranslationKey) => string, code: string): string {
  const key = `taxGroup.name.${code}`
  return key in en ? `${t(key as TranslationKey)} (${code})` : code
}
