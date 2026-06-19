import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '@/shared/auth/auth-context'
import { Spinner } from '@/components/ui/spinner'

export function ProtectedRoute() {
  const { status } = useAuth()

  if (status === 'loading') {
    return (
      <div className="flex h-screen items-center justify-center">
        <Spinner className="size-6 text-muted-foreground" />
      </div>
    )
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace />
  }

  return <Outlet />
}

export function PermissionRoute({ permission }: { permission: string }) {
  const { hasPermission } = useAuth()

  if (!hasPermission(permission)) {
    return <Navigate to="/account" replace />
  }

  return <Outlet />
}
