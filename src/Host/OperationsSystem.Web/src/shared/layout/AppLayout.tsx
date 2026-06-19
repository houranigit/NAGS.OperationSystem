import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '@/shared/auth/auth-context'
import { Button, cx } from '@/shared/ui'

const nav = [
  { to: '/users', label: 'Users' },
  { to: '/roles', label: 'Roles' },
]

export function AppLayout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const handleLogout = async () => {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="flex h-screen">
      <aside className="flex w-60 flex-col border-r border-slate-200 bg-white">
        <div className="px-5 py-4 text-lg font-semibold text-slate-900">Operations System</div>
        <nav className="flex-1 space-y-1 px-3">
          {nav.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                cx(
                  'block rounded-md px-3 py-2 text-sm font-medium',
                  isActive ? 'bg-slate-900 text-white' : 'text-slate-700 hover:bg-slate-100',
                )
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>
      <div className="flex flex-1 flex-col overflow-hidden">
        <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
          <div />
          <div className="flex items-center gap-3">
            <div className="text-right">
              <div className="text-sm font-medium text-slate-900">{user?.displayName}</div>
              <div className="text-xs text-slate-500">{user?.roleName}</div>
            </div>
            <Button variant="secondary" onClick={handleLogout}>
              Sign out
            </Button>
          </div>
        </header>
        <main className="flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
