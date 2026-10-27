import { useEffect } from 'react'
import { listHotels } from '../api/hotels'
import { useAsync } from '../hooks/useAsync'

interface Props {
  value: number | null
  onChange: (hotelId: number) => void
}

// Hotel picker for the housekeeping screens. Defaults to the first hotel returned.
export default function HotelSelect({ value, onChange }: Props) {
  const hotels = useAsync(listHotels, [])
  const first = hotels.data?.[0]?.id ?? null

  useEffect(() => {
    if (value === null && first !== null) onChange(first)
  }, [value, first, onChange])

  return (
    <div className="hotel-select">
      <label>
        Hotel{' '}
        <select
          value={value ?? ''}
          disabled={!hotels.data || hotels.data.length === 0}
          onChange={e => onChange(Number(e.target.value))}
        >
          {hotels.data?.map(h => <option key={h.id} value={h.id}>{h.name}</option>)}
        </select>
      </label>
      {hotels.loading && <span className="state">Loading hotels...</span>}
      {hotels.error && <span className="state error">Hotels unavailable: {hotels.error}</span>}
      {hotels.data?.length === 0 && <span className="state">No hotels found.</span>}
    </div>
  )
}
