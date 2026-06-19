import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { InvitedUser, PagedResult, User, UserListItem, UserStatus } from '@/shared/api/types'

const USERS_KEY = 'users'

export interface UsersQueryParams {
  page: number
  pageSize: number
  search: string
  status: UserStatus | ''
}

export function useUsers({ page, pageSize, search, status }: UsersQueryParams) {
  return useQuery({
    queryKey: [USERS_KEY, { page, pageSize, search, status }],
    queryFn: () =>
      api
        .get<PagedResult<UserListItem>>('/identity/users', {
          params: { page, pageSize, search: search || undefined, status: status || undefined },
        })
        .then((r) => r.data),
  })
}

export function useUser(id: string | undefined) {
  return useQuery({
    queryKey: [USERS_KEY, id],
    queryFn: () => api.get<User>(`/identity/users/${id}`).then((r) => r.data),
    enabled: !!id,
  })
}

function invalidateUsers(qc: ReturnType<typeof useQueryClient>) {
  return qc.invalidateQueries({ queryKey: [USERS_KEY] })
}

export function useInviteUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { email: string; displayName: string; roleId: string }) =>
      api.post<InvitedUser>('/identity/users/invite', body).then((r) => r.data),
    onSuccess: () => invalidateUsers(qc),
  })
}

export function useUpdateUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, displayName }: { id: string; displayName: string }) =>
      api.put(`/identity/users/${id}`, { displayName }),
    onSuccess: () => invalidateUsers(qc),
  })
}

export function useAssignRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, roleId }: { id: string; roleId: string }) =>
      api.put(`/identity/users/${id}/role`, { roleId }),
    onSuccess: () => invalidateUsers(qc),
  })
}

function useUserAction(action: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post(`/identity/users/${id}/${action}`),
    onSuccess: () => invalidateUsers(qc),
  })
}

export const useLockUser = () => useUserAction('lock')
export const useUnlockUser = () => useUserAction('unlock')
export const useDeactivateUser = () => useUserAction('deactivate')
export const useResendInvitation = () => useUserAction('resend-invitation')
