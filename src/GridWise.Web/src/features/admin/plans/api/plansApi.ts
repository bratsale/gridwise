import type { components } from '../../../../shared/api/catalog.ts'
import { safeRequest } from '../../../../shared/api/client.ts'

export type Plan = components['schemas']['PlanResponse']
export type PlanRequest = components['schemas']['CreatePlanRequest']

const call = <T>(path: string, options?: Parameters<typeof safeRequest>[2]) => safeRequest<T>('catalog', path, options)

export const listPlans = () => call<Plan[]>('/plans?includeRetired=true')

export const getPlan = (id: string) => call<Plan>(`/plans/${id}`)

export const listVersions = (id: string) => call<Plan[]>(`/plans/${id}/versions`)

export const createPlan = (body: PlanRequest, idempotencyKey: string) =>
  call<Plan>('/plans', { method: 'POST', body, headers: { 'Idempotency-Key': idempotencyKey } })

export const addVersion = (id: string, body: PlanRequest, etag: string) =>
  call<Plan>(`/plans/${id}/versions`, { method: 'POST', body, headers: { 'If-Match': etag } })

export const retirePlan = (id: string, effectiveFrom: string, etag: string) =>
  call<undefined>(`/plans/${id}?effectiveFrom=${effectiveFrom}`, { method: 'DELETE', headers: { 'If-Match': etag } })
