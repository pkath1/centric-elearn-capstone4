import { useState, type FormEvent } from 'react'
import { getBoard, getReadyBy, getRoom, setOutOfOrder, setRoomStatus } from '../api/rooms'
import type { BoardRoom, ReadyBy, RoomStatus } from '../api/types'
import HotelSelect from '../components/HotelSelect'
import StateView from '../components/StateView'
import { useAsync } from '../hooks/useAsync'
import { fmtClock, fmtDateTime, parseUserId } from '../format'

const BOARD_POLL_MS = 15000
const REASON_MAX_LENGTH = 255
const ENTER_USER_TEXT = 'Enter the acting staff user id (a whole number above 0).'

const STATUS_LABEL: Record<RoomStatus, string> = {
  Dirty: 'Dirty',
  Cleaning: 'Cleaning',
  Inspected: 'Inspected',
  Ready: 'Ready',
  OutOfOrder: 'Out of order',
}

// Next step on the standard path. The server decides whether the acting user may make it.
const NEXT_STATUS: Partial<Record<RoomStatus, RoomStatus>> = {
  Dirty: 'Cleaning',
  Cleaning: 'Inspected',
  Inspected: 'Ready',
  OutOfOrder: 'Dirty',
}

interface Notice {
  ok: boolean
  text: string
}

export default function BoardPage() {
  const [hotelId, setHotelId] = useState<number | null>(null)
  const [actingUser, setActingUser] = useState('')

  return (
    <section>
      <h1>Housekeeping board</h1>
      <div className="filters">
        <HotelSelect value={hotelId} onChange={setHotelId} />
        <label>
          Acting staff user id{' '}
          <input
            type="number"
            min={1}
            step={1}
            value={actingUser}
            onChange={e => setActingUser(e.target.value)}
          />
        </label>
      </div>
      {hotelId !== null && <BoardContent key={hotelId} hotelId={hotelId} actingUser={actingUser} />}
    </section>
  )
}

function BoardContent({ hotelId, actingUser }: { hotelId: number; actingUser: string }) {
  const board = useAsync(() => getBoard(hotelId), [hotelId], { pollMs: BOARD_POLL_MS })
  const [pendingRoomId, setPendingRoomId] = useState<number | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  function finish(result: Notice) {
    setNotice(result)
    board.reload()
  }

  async function moveRoom(room: BoardRoom, next: RoomStatus) {
    const userId = parseUserId(actingUser)
    if (userId === null) {
      setNotice({ ok: false, text: ENTER_USER_TEXT })
      return
    }
    setPendingRoomId(room.roomId)
    setNotice(null)
    try {
      // The board does not carry rowVersion, so read the room first.
      const current = await getRoom(room.roomId)
      await setRoomStatus(room.roomId, {
        status: next,
        changedByUserId: userId,
        rowVersion: current.rowVersion,
        reason: null,
      })
      finish({ ok: true, text: `Room ${room.roomNumber} is now ${STATUS_LABEL[next]}.` })
    } catch (e) {
      finish({ ok: false, text: `Room ${room.roomNumber} was not moved. ${(e as Error).message}` })
    } finally {
      setPendingRoomId(null)
    }
  }

  return (
    <>
    <StateView
      state={board}
      loadingText="Loading board"
      isEmpty={d => d.floors.every(f => f.rooms.length === 0)}
      emptyText="No rooms at this hotel"
    >
      {data => {
        const rooms = data.floors.flatMap(f => f.rooms)
        return (
          <>
            {notice && <p className={`notice ${notice.ok ? 'ok' : 'error'}`} role="status">{notice.text}</p>}
            {data.floors.map(floor => (
              <section
                key={floor.floor}
                className={floor.busy ? 'floor busy' : 'floor'}
                aria-labelledby={`floor-${floor.floor}`}
              >
                <h2 id={`floor-${floor.floor}`}>
                  Floor {floor.floor}
                  {floor.busy && <span className="badge busy-badge">Busiest floor</span>}
                </h2>
                <ul className="room-grid">
                  {floor.rooms.map(room => {
                    const next = NEXT_STATUS[room.status]
                    const actionText = room.status === 'OutOfOrder' ? 'Release to Dirty' : `Move to ${next ? STATUS_LABEL[next] : ''}`
                    return (
                      <li key={room.roomId} className="room-tile">
                        <strong>Room {room.roomNumber}</strong>
                        <span>{room.roomType}</span>
                        <span className={`badge status-${room.status}`}>{STATUS_LABEL[room.status]}</span>
                        <span>SLA target: {room.targetMinutes !== null ? `${room.targetMinutes} min` : '-'}</span>
                        <span>
                          {room.status === 'Ready' ? 'Ready at' : 'Ready by'}:{' '}
                          {room.readyBy ? fmtClock(room.readyBy) : '-'}
                        </span>
                        {next && (
                          <button
                            type="button"
                            disabled={pendingRoomId === room.roomId}
                            onClick={() => moveRoom(room, next)}
                          >
                            {actionText}
                          </button>
                        )}
                      </li>
                    )
                  })}
                </ul>
              </section>
            ))}
            <div className="board-tools">
              <ReadyByLookup rooms={rooms} />
              <OutOfOrderForm rooms={rooms} actingUser={actingUser} onDone={finish} />
            </div>
          </>
        )
      }}
    </StateView>
    {board.error && <button type="button" onClick={board.reload}>Retry</button>}
    </>
  )
}

function describeReadyBy(answer: ReadyBy): string {
  if (answer.outOfOrder) {
    return answer.returnsAt
      ? `Out of order until ${fmtDateTime(answer.returnsAt)}`
      : `Out of order, return time not known`
  }
  if (answer.readyBy) {
    const overdue = answer.overdue ? ' (turnaround target has passed)' : ''
    return `Ready at ${fmtClock(answer.readyBy)}${overdue}`
  }
  return `Status ${STATUS_LABEL[answer.status]}, no ready time yet`
}

function ReadyByLookup({ rooms }: { rooms: BoardRoom[] }) {
  const [roomNumber, setRoomNumber] = useState('')
  const [answer, setAnswer] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function lookup(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const wanted = roomNumber.trim()
    setAnswer(null)
    if (wanted === '') {
      setError('Enter a room number.')
      return
    }
    const room = rooms.find(r => r.roomNumber === wanted)
    if (!room) {
      setError(`No room ${wanted} at this hotel.`)
      return
    }
    setBusy(true)
    setError(null)
    try {
      setAnswer(describeReadyBy(await getReadyBy(room.roomId)))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="tool">
      <h2>Ready-by lookup</h2>
      <form onSubmit={lookup}>
        <label>
          Room number{' '}
          <input value={roomNumber} onChange={e => setRoomNumber(e.target.value)} />
        </label>{' '}
        <button type="submit" disabled={busy}>Look up</button>
      </form>
      {answer && <p className="notice ok" role="status">{answer}</p>}
      {error && <p className="notice error" role="alert">{error}</p>}
    </section>
  )
}

interface OutOfOrderFormProps {
  rooms: BoardRoom[]
  actingUser: string
  onDone: (notice: Notice) => void
}

function OutOfOrderForm({ rooms, actingUser, onDone }: OutOfOrderFormProps) {
  const [roomId, setRoomId] = useState('')
  const [reason, setReason] = useState('')
  const [expectedReturn, setExpectedReturn] = useState('')
  const [problems, setProblems] = useState<string[]>([])
  const [saving, setSaving] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const userId = parseUserId(actingUser)
    const trimmedReason = reason.trim()
    const found: string[] = []
    if (userId === null) found.push(ENTER_USER_TEXT)
    if (roomId === '') found.push('Choose a room.')
    if (trimmedReason === '') found.push('Reason is required.')
    else if (trimmedReason.length > REASON_MAX_LENGTH) found.push(`Reason must be ${REASON_MAX_LENGTH} characters or fewer.`)
    if (expectedReturn !== '' && Number.isNaN(new Date(expectedReturn).getTime())) {
      found.push('Expected return is not a valid date and time.')
    }
    setProblems(found)
    if (found.length > 0 || userId === null) return

    const id = Number(roomId)
    const room = rooms.find(r => r.roomId === id)
    setSaving(true)
    try {
      const current = await getRoom(id)
      await setOutOfOrder(id, {
        changedByUserId: userId,
        reason: trimmedReason,
        expectedReturn: expectedReturn === '' ? null : new Date(expectedReturn).toISOString(),
        rowVersion: current.rowVersion,
      })
      setReason('')
      setExpectedReturn('')
      setProblems([])
      onDone({ ok: true, text: `Room ${room?.roomNumber ?? id} is now out of order.` })
    } catch (err) {
      onDone({ ok: false, text: `Room ${room?.roomNumber ?? id} was not set out of order. ${(err as Error).message}` })
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="tool">
      <h2>Set out of order</h2>
      <form onSubmit={submit} noValidate>
        <div className="form-grid">
          <label>
            Room{' '}
            <select value={roomId} onChange={e => setRoomId(e.target.value)}>
              <option value="">Choose a room</option>
              {rooms.map(r => (
                <option key={r.roomId} value={r.roomId}>
                  {r.roomNumber} ({STATUS_LABEL[r.status]})
                </option>
              ))}
            </select>
          </label>
          <label>
            Reason (required, max {REASON_MAX_LENGTH} characters){' '}
            <input value={reason} maxLength={REASON_MAX_LENGTH} onChange={e => setReason(e.target.value)} />
          </label>
          <label>
            Expected return (optional){' '}
            <input type="datetime-local" value={expectedReturn} onChange={e => setExpectedReturn(e.target.value)} />
          </label>
        </div>
        {problems.length > 0 && (
          <ul className="field-errors" role="alert">
            {problems.map(p => <li key={p}>{p}</li>)}
          </ul>
        )}
        <button type="submit" disabled={saving}>Set out of order</button>
      </form>
    </section>
  )
}
