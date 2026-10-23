/** Tipos que espelham os DTOs da API (camelCase). */

export type Role = 'admin' | 'operator'
export type UserStatus = 'active' | 'disabled'

export interface Me {
  id: string
  name: string
  email: string
  role: Role
}

export interface AppUser {
  id: string
  name: string
  email: string
  role: Role
  status: UserStatus
  createdAt: string
  lastLoginAt: string | null
  isYou: boolean
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}
