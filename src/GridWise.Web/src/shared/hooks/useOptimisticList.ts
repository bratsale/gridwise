import { useCallback, useRef, useState } from 'react'

export interface Optimistic<T> {
  // Shows `expected` in the list at once and marks it pending. It replaces the item with the same key,
  // or is added when there is none. commit() puts the server's version in its place, rollback() restores the list as it was.
  begin: (expected: T) => { commit: (actual?: T) => void; rollback: () => void }
}

// A list that can show an expected change before the server has answered.
export function useOptimisticList<T>(keyOf: (item: T) => string, compare?: (a: T, b: T) => number) {
  const [items, setItemsState] = useState<T[]>([])
  const itemsRef = useRef<T[]>([])
  const [pending, setPending] = useState<ReadonlySet<string>>(new Set())

  // Stable, so effects that load the list do not run again on every render.
  const setItems = useCallback((next: T[]) => {
    itemsRef.current = next
    setItemsState(next)
  }, [])

  function setPendingKey(key: string, on: boolean) {
    setPending((current) => {
      const next = new Set(current)
      if (on) next.add(key)
      else next.delete(key)
      return next
    })
  }

  const optimistic: Optimistic<T> = {
    begin(expected) {
      const snapshot = itemsRef.current
      const key = keyOf(expected)
      const exists = snapshot.some((item) => keyOf(item) === key)
      const next = exists ? snapshot.map((item) => (keyOf(item) === key ? expected : item)) : [expected, ...snapshot]
      setItems(compare ? [...next].sort(compare) : next)
      setPendingKey(key, true)

      return {
        commit(actual) {
          if (actual) {
            const replaced = itemsRef.current.map((item) => (keyOf(item) === key ? actual : item))
            setItems(compare ? [...replaced].sort(compare) : replaced)
          }
          setPendingKey(key, false)
        },
        rollback() {
          setItems(snapshot)
          setPendingKey(key, false)
        },
      }
    },
  }

  return { items, setItems, pending, optimistic }
}
