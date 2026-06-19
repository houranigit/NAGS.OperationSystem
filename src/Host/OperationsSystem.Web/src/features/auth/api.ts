import { api } from '@/shared/api/client'
import type { AccessTokenResponse, AuthenticatedUser } from '@/shared/api/types'

export const authApi = {
  login: (email: string, password: string) =>
    api.post<AccessTokenResponse>('/identity/auth/login', { email, password }).then((r) => r.data),
  logout: () => api.post('/identity/auth/logout').then(() => undefined),
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post('/identity/auth/change-password', { currentPassword, newPassword }).then(() => undefined),
  activate: (email: string, invitationToken: string, newPassword: string) =>
    api.post('/identity/auth/activate', { email, invitationToken, newPassword }).then(() => undefined),
  me: () => api.get<AuthenticatedUser>('/identity/me').then((r) => r.data),
}
