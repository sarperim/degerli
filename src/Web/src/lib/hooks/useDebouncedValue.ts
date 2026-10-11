import { useEffect, useState } from 'react'

/**
 * Debounce a rapidly-changing value (e.g. a search term driving a network query).
 *
 * Used by the header typeahead so keystrokes do not fire a request each; the
 * test suite advances timers or asserts on the settled value.
 */
export function useDebouncedValue<T>(value: T, delayMs = 250): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}
