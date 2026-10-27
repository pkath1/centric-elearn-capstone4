import type { ReactNode } from 'react'
import type { AsyncState } from '../hooks/useAsync'

interface Props<T> {
  state: AsyncState<T>
  isEmpty: (data: T) => boolean
  emptyText: string
  loadingText?: string
  children: (data: T) => ReactNode
}

export default function StateView<T>({ state, isEmpty, emptyText, loadingText = 'Loading...', children }: Props<T>) {
  if (state.loading) return <p className="state">{loadingText}</p>
  if (state.error) return <p className="state error">Something went wrong: {state.error}</p>
  if (!state.data || isEmpty(state.data)) return <p className="state">{emptyText}</p>
  return <>{children(state.data)}</>
}
