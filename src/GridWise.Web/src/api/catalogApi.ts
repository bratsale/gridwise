import type { components } from './catalog.ts'
import { request, type ApiResponse, type RequestOptions } from './client.ts'

export type Plan = components['schemas']['PlanResponse']
export type PlanRequest = components['schemas']['CreatePlanRequest']

// Like request(), but a network failure or a non-JSON answer becomes a ProblemDetails instead of an exception.
async function call<T>(path: string, options?: RequestOptions): Promise<ApiResponse<T>> {
  try {
    return await request<T>('catalog', path, options)
  } catch {
    return { ok: false, status: 0, headers: new Headers(), body: { title: 'The service could not be reached.' } }
  }
}

export const listPlans = () => call<Plan[]>('/plans?includeRetired=true')

export const getPlan = (id: string) => call<Plan>(`/plans/${id}`)

export const listVersions = (id: string) => call<Plan[]>(`/plans/${id}/versions`)

export const createPlan = (body: PlanRequest, idempotencyKey: string) =>
  call<Plan>('/plans', { method: 'POST', body, headers: { 'Idempotency-Key': idempotencyKey } })

export const addVersion = (id: string, body: PlanRequest, etag: string) =>
  call<Plan>(`/plans/${id}/versions`, { method: 'POST', body, headers: { 'If-Match': etag } })

export const retirePlan = (id: string, effectiveFrom: string, etag: string) =>
  call<undefined>(`/plans/${id}?effectiveFrom=${effectiveFrom}`, { method: 'DELETE', headers: { 'If-Match': etag } })

export const CONFLICT_MESSAGE =
  'This plan was changed by someone else (or retired). Reload it and apply your change again.'
