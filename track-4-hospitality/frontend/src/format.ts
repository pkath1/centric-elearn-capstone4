// Display helpers for the housekeeping screens. Server times are UTC ISO 8601;
// the browser shows them in local time.

export function fmtClock(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

export function fmtDateTime(iso: string): string {
  const date = new Date(iso)
  const day = date.toLocaleDateString([], { day: 'numeric', month: 'long' })
  return `${day}, ${fmtClock(iso)}`
}

// Returns the staff user id as a positive whole number, or null when invalid.
export function parseUserId(raw: string): number | null {
  const value = Number(raw)
  return raw.trim() !== '' && Number.isInteger(value) && value > 0 ? value : null
}
