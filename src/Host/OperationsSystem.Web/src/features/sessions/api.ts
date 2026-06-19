import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { UserSession } from '@/shared/api/types'

const SESSIONS_KEY = 'sessions'
const MY_SESSIONS_KEY = 'my-sessions'

// --- Admin: per-user sessions ---------------------------------------------

export function useUserSessions(userId: string | undefined) {
  return useQuery({
    queryKey: [SESSIONS_KEY, userId],
    queryFn: () => api.get<UserSession[]>(`/identity/users/${userId}/sessions`).then((r) => r.data),
    enabled: !!userId,
  })
}

export function useRevokeSession() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (sessionId: string) => api.delete(`/identity/sessions/${sessionId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: [SESSIONS_KEY] }),
  })
}

export function useRevokeUserSessions() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (userId: string) => api.post(`/identity/users/${userId}/sessions/revoke-all`),
    onSuccess: () => qc.invalidateQueries({ queryKey: [SESSIONS_KEY] }),
  })
}

// --- Self-service: my own sessions ----------------------------------------

export function useMySessions() {
  return useQuery({
    queryKey: [MY_SESSIONS_KEY],
    queryFn: () => api.get<UserSession[]>('/identity/me/sessions').then((r) => r.data),
  })
}

export function useRevokeMySession() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (sessionId: string) => api.delete(`/identity/me/sessions/${sessionId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: [MY_SESSIONS_KEY] }),
  })
}

export function useRevokeMyOtherSessions() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.post('/identity/me/sessions/revoke-others'),
    onSuccess: () => qc.invalidateQueries({ queryKey: [MY_SESSIONS_KEY] }),
  })
}
