export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

export interface AccessTokenResponse {
  accessToken: string
  expiresAtUtc: string
}

export interface AuthenticatedUser {
  id: string
  email: string
  displayName: string
  roleId: string
  roleName: string
  permissions: string[]
}

export interface RoleListItem {
  id: string
  name: string
  description: string | null
  isSystem: boolean
  permissionCount: number
  userCount: number
}

export interface Role {
  id: string
  name: string
  description: string | null
  isSystem: boolean
  permissions: string[]
  createdAtUtc: string
  updatedAtUtc: string | null
}

export interface PermissionGroup {
  resource: string
  permissions: string[]
}

export type UserStatus = 'Invited' | 'Active' | 'Deactivated'

export interface UserListItem {
  id: string
  email: string
  displayName: string
  status: UserStatus
  isLockedOut: boolean
  roleId: string
  roleName: string
  createdAtUtc: string
  lastLoginAtUtc: string | null
}

export interface InvitedUser {
  id: string
  email: string
  invitationToken: string
}
