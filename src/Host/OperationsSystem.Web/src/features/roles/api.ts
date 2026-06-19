import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { PagedResult, PermissionGroup, Role, RoleListItem } from '@/shared/api/types'

const ROLES_KEY = 'roles'

export function useRoles(search: string) {
  return useQuery({
    queryKey: [ROLES_KEY, { search }],
    queryFn: () =>
      api
        .get<PagedResult<RoleListItem>>('/identity/roles', { params: { pageSize: 100, search: search || undefined } })
        .then((r) => r.data),
  })
}

export function useRole(id: string | null) {
  return useQuery({
    queryKey: [ROLES_KEY, id],
    queryFn: () => api.get<Role>(`/identity/roles/${id}`).then((r) => r.data),
    enabled: !!id,
  })
}

export function usePermissionCatalog() {
  return useQuery({
    queryKey: ['permissions'],
    queryFn: () => api.get<PermissionGroup[]>('/identity/permissions').then((r) => r.data),
  })
}

export function useCreateRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { name: string; description: string | null; permissions: string[] }) =>
      api.post('/identity/roles', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: [ROLES_KEY] }),
  })
}

export function useUpdateRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string; name: string; description: string | null }) =>
      api.put(`/identity/roles/${id}`, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: [ROLES_KEY] }),
  })
}

export function useUpdateRolePermissions() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, permissions }: { id: string; permissions: string[] }) =>
      api.put(`/identity/roles/${id}/permissions`, { permissions }),
    onSuccess: () => qc.invalidateQueries({ queryKey: [ROLES_KEY] }),
  })
}

export function useDeleteRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete(`/identity/roles/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: [ROLES_KEY] }),
  })
}
