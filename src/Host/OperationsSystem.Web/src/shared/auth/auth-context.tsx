import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { authApi } from '@/features/auth/api'
import { refreshAccessToken, setAccessToken } from '@/shared/api/client'
import type { AuthenticatedUser } from '@/shared/api/types'

type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

interface AuthContextValue {
  status: AuthStatus
  user: AuthenticatedUser | null
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  hasPermission: (permission: string) => boolean
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<AuthenticatedUser | null>(null)

  useEffect(() => {
    let cancelled = false
    void (async () => {
      const token = await refreshAccessToken()
      if (cancelled) return
      if (!token) {
        setStatus('anonymous')
        return
      }
      try {
        const me = await authApi.me()
        if (cancelled) return
        setUser(me)
        setStatus('authenticated')
      } catch {
        if (!cancelled) setStatus('anonymous')
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      login: async (email, password) => {
        const tokens = await authApi.login(email, password)
        setAccessToken(tokens.accessToken)
        const me = await authApi.me()
        setUser(me)
        setStatus('authenticated')
      },
      logout: async () => {
        try {
          await authApi.logout()
        } finally {
          setAccessToken(null)
          setUser(null)
          setStatus('anonymous')
        }
      },
      hasPermission: (permission) => user?.permissions.includes(permission) ?? false,
    }),
    [status, user],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
