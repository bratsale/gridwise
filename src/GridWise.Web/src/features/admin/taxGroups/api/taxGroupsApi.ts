import type { components } from '../../../../shared/api/catalog.ts'
import { safeRequest } from '../../../../shared/api/client.ts'

export type TaxGroup = components['schemas']['TaxGroupResponse']
export type CreateTaxGroupRequest = components['schemas']['CreateTaxGroupRequest']
export type TaxGroupVersionRequest = components['schemas']['TaxGroupVersionRequest']

const call = <T>(path: string, options?: Parameters<typeof safeRequest>[2]) => safeRequest<T>('catalog', path, options)

export const listTaxGroups = () => call<TaxGroup[]>('/tax-groups?includeRetired=true')

export const getTaxGroup = (code: string) => call<TaxGroup>(`/tax-groups/${code}`)

export const listVersions = (code: string) => call<TaxGroup[]>(`/tax-groups/${code}/versions`)

export const createTaxGroup = (body: CreateTaxGroupRequest) => call<TaxGroup>('/tax-groups', { method: 'POST', body })

export const addVersion = (code: string, body: TaxGroupVersionRequest, etag: string) =>
  call<TaxGroup>(`/tax-groups/${code}/versions`, { method: 'POST', body, headers: { 'If-Match': etag } })

export const retireTaxGroup = (code: string, effectiveFrom: string, etag: string) =>
  call<undefined>(`/tax-groups/${code}?effectiveFrom=${effectiveFrom}`, { method: 'DELETE', headers: { 'If-Match': etag } })
