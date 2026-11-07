import { msg, type Message } from '../../shared/i18n/message.ts'

export type CustomerField = 'consumptionKwh' | 'taxGroup'

// Client-side checks before sending; the server stays authoritative.
export function validateCustomer(values: { consumption: string; taxGroup: string }): Partial<Record<CustomerField, Message>> {
  const errors: Partial<Record<CustomerField, Message>> = {}
  const text = values.consumption.trim()
  const number = Number(text)

  if (text === '' || Number.isNaN(number)) errors.consumptionKwh = msg('customer.validation.consumptionRequired')
  else if (number < 0) errors.consumptionKwh = msg('customer.validation.consumptionMin')
  else if (!/e/i.test(text) && (text.split('.')[1] ?? '').length > 3) errors.consumptionKwh = msg('customer.validation.consumptionDecimals')

  if (values.taxGroup === '') errors.taxGroup = msg('customer.validation.taxGroupRequired')
  return errors
}
