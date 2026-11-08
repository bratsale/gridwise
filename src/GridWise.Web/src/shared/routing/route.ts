import { useCallback, useMemo, useSyncExternalStore } from 'react'

export type AdminSection = 'plans' | 'taxGroups' | 'batch'
export type Route = { tab: 'customer' } | { tab: 'admin'; section: AdminSection }

// #customer, #admin/plans, #admin/tax-groups, #admin/batch. Anything else (including no hash) is the plans page.
export function parseRoute(hash: string): Route {
  const path = hash.replace(/^#\/?/, '')
  if (path === 'customer') return { tab: 'customer' }
  if (path === 'admin/tax-groups') return { tab: 'admin', section: 'taxGroups' }
  if (path === 'admin/batch') return { tab: 'admin', section: 'batch' }
  return { tab: 'admin', section: 'plans' }
}

export function routeToHash(route: Route): string {
  if (route.tab === 'customer') return '#customer'
  if (route.section === 'taxGroups') return '#admin/tax-groups'
  return route.section === 'batch' ? '#admin/batch' : '#admin/plans'
}

function subscribe(onChange: () => void) {
  window.addEventListener('hashchange', onChange)
  return () => window.removeEventListener('hashchange', onChange)
}

// The route lives in the URL hash, so reload and back/forward keep it.
export function useRoute(): [Route, (route: Route) => void] {
  const hash = useSyncExternalStore(subscribe, () => window.location.hash)
  const route = useMemo(() => parseRoute(hash), [hash])
  const navigate = useCallback((next: Route) => {
    window.location.hash = routeToHash(next)
  }, [])
  return [route, navigate]
}
