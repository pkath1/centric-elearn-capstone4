import { useState } from 'react'
import { getSlaBreaches } from '../api/rooms'
import HotelSelect from '../components/HotelSelect'
import StateView from '../components/StateView'
import { useAsync } from '../hooks/useAsync'
import { fmtClock } from '../format'

export default function SlaBreachesPage() {
  const [hotelId, setHotelId] = useState<number | null>(null)

  return (
    <section>
      <h1>SLA breaches</h1>
      <div className="filters">
        <HotelSelect value={hotelId} onChange={setHotelId} />
      </div>
      {hotelId !== null && <BreachTable key={hotelId} hotelId={hotelId} />}
    </section>
  )
}

function BreachTable({ hotelId }: { hotelId: number }) {
  const breaches = useAsync(() => getSlaBreaches(hotelId), [hotelId])

  return (
    <>
      <StateView
        state={breaches}
        loadingText="Loading breaches"
        isEmpty={d => d.items.length === 0}
        emptyText="No breaches right now"
      >
        {data => {
          // Worst first: largest overrun at the top. The server orders the same way.
          const items = [...data.items].sort((a, b) => b.overrunMinutes - a.overrunMinutes)
          return (
            <table>
              <thead>
                <tr>
                  <th>Room</th><th>Type</th><th>Checkout</th><th>Target (min)</th><th>Elapsed (min)</th><th>Overrun (min)</th>
                </tr>
              </thead>
              <tbody>
                {items.map(b => (
                  <tr key={b.roomId}>
                    <td>{b.roomNumber}</td>
                    <td>{b.roomType}</td>
                    <td>{fmtClock(b.checkoutAt)}</td>
                    <td>{b.targetMinutes}</td>
                    <td>{b.elapsedMinutes}</td>
                    <td>{b.overrunMinutes}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
        }}
      </StateView>
      {breaches.error && (
        <button type="button" onClick={breaches.reload}>Retry</button>
      )}
    </>
  )
}
