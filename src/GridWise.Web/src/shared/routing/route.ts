import { useCallback, useMemo, useSyncExternalStore } from 'react'

export type AdminSection = 'plans' | 'taxGroups'
export type Route = { tab: 'customer' } | { tab: 'admin'; section: AdminSection }

// #customer, #admin/plans, #admin/tax-groups. Anything else (including no hash) is the plans page.
export function parseRoute(hash: string): Route {
  const path = hash.replace(/^#\/?/, '')
  if (path === 'customer') return { tab: 'customer' }
  return { tab: 'admin', section: path === 'admin/tax-groups' ? 'taxGroups' : 'plans' }
}

export function routeToHash(route: Route): string {
  if (route.tab === 'customer') return '#customer'
  return route.section === 'taxGroups' ? '#admin/tax-groups' : '#admin/plans'
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
