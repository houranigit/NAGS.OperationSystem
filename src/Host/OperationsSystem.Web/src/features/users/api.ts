import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { InvitedUser, PagedResult, UserListItem, UserStatus } from '@/shared/api/types'

const USERS_KEY = 'users'

export function useUsers(search: string, status: UserStatus | '') {
  return useQuery({
    queryKey: [USERS_KEY, { search, status }],
    queryFn: () =>
      api
        .get<PagedResult<UserListItem>>('/identity/users', {
          params: { pageSize: 100, search: search || undefined, status: status || undefined },
        })
        .then((r) => r.data),
  })
}

export function useInviteUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { email: string; displayName: string; roleId: string }) =>
      api.post<InvitedUser>('/identity/users/invite', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [USERS_KEY] }),
  })
}

export function useUpdateUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, displayName }: { id: string; displayName: string }) =>
      api.put(`/identity/users/${id}`, { displayName }),
    onSuccess: () => qc.invalidateQueries({ queryKey: [USERS_KEY] }),
  })
}

export function useAssignRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, roleId }: { id: string; roleId: string }) =>
      api.put(`/identity/users/${id}/role`, { roleId }),
    onSuccess: () => qc.invalidateQueries({ queryKey: [USERS_KEY] }),
  })
}

function useUserAction(action: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post(`/identity/users/${id}/${action}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: [USERS_KEY] }),
  })
}

export const useLockUser = () => useUserAction('lock')
export const useUnlockUser = () => useUserAction('unlock')
export const useDeactivateUser = () => useUserAction('deactivate')
export const useResendInvitation = () => useUserAction('resend-invitation')
