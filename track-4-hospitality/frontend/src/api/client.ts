export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  let message = response.statusText
  try {
    const problem = await response.json()
    message = problem.detail ?? problem.title ?? message
  } catch {
    /* body was not JSON */
  }
  return new ApiError(response.status, message)
}

export async function getJson<T>(path: string, params?: Record<string, string | number | undefined>): Promise<T> {
  const query = new URLSearchParams()
  Object.entries(params ?? {}).forEach(([key, value]) => {
    if (value !== undefined && value !== '') query.set(key, String(value))
  })
  const qs = query.toString()
  const response = await fetch(qs ? `${path}?${qs}` : path)
  if (!response.ok) throw await toApiError(response)
  return response.json() as Promise<T>
}

async function sendJson<T>(method: 'POST' | 'PUT', path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw await toApiError(response)
  return response.json() as Promise<T>
}

export const postJson = <T>(path: string, body: unknown) => sendJson<T>('POST', path, body)

export const putJson = <T>(path: string, body: unknown) => sendJson<T>('PUT', path, body)
