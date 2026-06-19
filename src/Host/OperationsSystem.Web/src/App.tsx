import { Navigate, Route, Routes } from 'react-router-dom'
import { ProtectedRoute } from '@/shared/auth/ProtectedRoute'
import { AppLayout } from '@/shared/layout/AppLayout'
import { LoginPage } from '@/features/auth/LoginPage'
import { UsersPage } from '@/features/users/UsersPage'
import { RolesPage } from '@/features/roles/RolesPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route index element={<Navigate to="/users" replace />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/roles" element={<RolesPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
