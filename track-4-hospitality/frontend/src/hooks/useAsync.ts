import { useCallback, useEffect, useRef, useState } from 'react'

export interface AsyncState<T> {
  data: T | null
  loading: boolean
  error: string | null
}

interface AsyncOptions {
  /** Re-run the load silently every pollMs while the tab is visible. */
  pollMs?: number
}

export function useAsync<T>(
  load: () => Promise<T>,
  deps: unknown[],
  options?: AsyncOptions,
): AsyncState<T> & { reload: () => void } {
  const [state, setState] = useState<AsyncState<T>>({ data: null, loading: true, error: null })
  const [reloadCount, setReloadCount] = useState(0)
  const loadRef = useRef(load)
  const pollMs = options?.pollMs

  useEffect(() => {
    loadRef.current = load
  }, [load])

  useEffect(() => {
    let cancelled = false
    setState(prev => ({ ...prev, loading: true, error: null }))
    load()
      .then(data => !cancelled && setState({ data, loading: false, error: null }))
      .catch((e: Error) => !cancelled && setState({ data: null, loading: false, error: e.message }))
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, reloadCount])

  useEffect(() => {
    if (!pollMs) return
    let active = true
    const timer = setInterval(() => {
      if (document.hidden) return
      loadRef.current()
        .then(data => active && setState({ data, loading: false, error: null }))
        .catch((e: Error) => active && setState({ data: null, loading: false, error: e.message }))
    }, pollMs)
    return () => {
      active = false
      clearInterval(timer)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pollMs, ...deps])

  const reload = useCallback(() => setReloadCount(count => count + 1), [])

  return { ...state, reload }
}
