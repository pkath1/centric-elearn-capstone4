export interface Hotel {
  id: number
  code: string
  name: string
  city: string
  region: string
  addressLine: string
  phone: string
  active: boolean
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface Booking {
  id: number
  reference: string
  channel: string
  hotelName: string
  guestName: string
  roomTypeName: string
  roomNumber: string | null
  checkIn: string
  checkOut: string
  nights: number
  status: string
  totalAmount: number
}

export interface Guest {
  id: number
  firstName: string
  lastName: string
  email: string
  phone: string
  city: string
  region: string
  loyaltyTier: string
}

export interface OccupancyReport {
  hotelId: number | null
  from: string
  to: string
  roomsAvailable: number
  roomNightsSold: number
  occupancyPct: number
  days: { date: string; roomsSold: number }[]
}

export type RoomStatus = 'Dirty' | 'Cleaning' | 'Inspected' | 'Ready' | 'OutOfOrder'

export interface BoardRoom {
  roomId: number
  roomNumber: string
  roomType: string
  status: RoomStatus
  checkoutAt: string | null
  targetMinutes: number | null
  readyBy: string | null
  housekeeperUserId: number | null
}

export interface BoardFloor {
  floor: number
  busy: boolean
  rooms: BoardRoom[]
}

export interface HotelBoard {
  hotelId: number
  busiestFloor: number | null
  floors: BoardFloor[]
}

export interface SlaBreach {
  roomId: number
  roomNumber: string
  roomType: string
  checkoutAt: string
  readyAt: string | null
  targetMinutes: number
  elapsedMinutes: number
  overrunMinutes: number
}

export interface SlaBreachList {
  items: SlaBreach[]
}

export interface ReadyBy {
  roomId: number
  roomNumber: string
  status: RoomStatus
  readyBy: string | null
  overdue: boolean
  outOfOrder: boolean
  returnsAt: string | null
}

// Room as returned by GET /api/rooms/{id} and PUT /api/rooms/{id}/status.
export interface RoomDetail {
  id: number
  hotelId: number
  roomTypeId: number
  roomTypeName: string
  roomNumber: string
  floor: number
  status: RoomStatus
  notes: string | null
  rowVersion: number
  simpleTurn: boolean
  oooReason: string | null
  oooSince: string | null
  oooExpectedReturn: string | null
}

export interface RoomStatusBody {
  status: RoomStatus
  changedByUserId: number
  rowVersion: number
  reason: string | null
}

export interface OutOfOrderBody {
  changedByUserId: number
  reason: string
  expectedReturn: string | null
  rowVersion: number
}

export interface SpotCheck {
  roomId: number
  roomNumber: string
  readyAt: string
  changedByUserId: number | null
}

export interface SpotCheckList {
  items: SpotCheck[]
}
