import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '@/shared/auth/auth-context'
import { Spinner } from '@/shared/ui'

export function ProtectedRoute() {
  const { status } = useAuth()

  if (status === 'loading') {
    return (
      <div className="flex h-screen items-center justify-center">
        <Spinner />
      </div>
    )
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace />
  }

  return <Outlet />
}
