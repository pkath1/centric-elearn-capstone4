import { getJson, postJson, putJson } from './client'
import type {
  HotelBoard,
  OutOfOrderBody,
  ReadyBy,
  RoomDetail,
  RoomStatusBody,
  SlaBreachList,
  SpotCheckList,
} from './types'

export const getBoard = (hotelId: number) => getJson<HotelBoard>(`/api/hotels/${hotelId}/board`)

export const getSlaBreaches = (hotelId: number) => getJson<SlaBreachList>(`/api/hotels/${hotelId}/sla-breaches`)

export const getReadyBy = (roomId: number) => getJson<ReadyBy>(`/api/rooms/${roomId}/ready-by`)

export const getRoom = (roomId: number) => getJson<RoomDetail>(`/api/rooms/${roomId}`)

export const setRoomStatus = (roomId: number, body: RoomStatusBody) =>
  putJson<RoomDetail>(`/api/rooms/${roomId}/status`, body)

export const setOutOfOrder = (roomId: number, body: OutOfOrderBody) =>
  postJson<RoomDetail>(`/api/rooms/${roomId}/out-of-order`, body)

export const listSpotChecks = (hotelId: number) => getJson<SpotCheckList>(`/api/hotels/${hotelId}/spot-checks`)
