import { Navigate, Route, Routes } from 'react-router-dom'
import { ProtectedRoute, PermissionRoute } from '@/shared/auth/ProtectedRoute'
import { AppLayout } from '@/shared/layout/AppLayout'
import { LoginPage } from '@/features/auth/LoginPage'
import { ActivatePage } from '@/features/auth/ActivatePage'
import { UsersPage } from '@/features/users/UsersPage'
import { UserDetailPage } from '@/features/users/UserDetailPage'
import { RolesPage } from '@/features/roles/RolesPage'
import { AccountPage } from '@/features/account/AccountPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/activate" element={<ActivatePage />} />
      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route index element={<Navigate to="/users" replace />} />
          <Route element={<PermissionRoute permission="identity.users.view" />}>
            <Route path="/users" element={<UsersPage />} />
            <Route path="/users/:id" element={<UserDetailPage />} />
          </Route>
          <Route element={<PermissionRoute permission="identity.roles.view" />}>
            <Route path="/roles" element={<RolesPage />} />
          </Route>
          <Route path="/account" element={<AccountPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
