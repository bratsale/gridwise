import type { components as CatalogTypes } from '../../../shared/api/catalog.ts'
import type { components as RecommendationTypes } from '../../../shared/api/recommendation.ts'
import { safeRequest } from '../../../shared/api/client.ts'

export type TaxGroup = CatalogTypes['schemas']['TaxGroupResponse']
export type Recommendation = RecommendationTypes['schemas']['RecommendationResponse']
export type Quote = RecommendationTypes['schemas']['PlanQuoteResponse']
export type Acceptance = RecommendationTypes['schemas']['AcceptanceResponse']
export type Breakdown = RecommendationTypes['schemas']['BreakdownResponse']

export const listTaxGroups = () => safeRequest<TaxGroup[]>('catalog', '/tax-groups')

export const calculate = (consumptionKwh: number, taxGroup: string) =>
  safeRequest<Recommendation>('recommendation', '/recommendations', { method: 'POST', body: { consumptionKwh, taxGroup } })

export const acceptRecommendation = (recommendationId: string, planId: string) =>
  safeRequest<Acceptance>('recommendation', `/recommendations/${recommendationId}/accept`, { method: 'POST', body: { planId } })
