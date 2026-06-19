import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { LogOutIcon, ShieldCheckIcon, UsersIcon, UserCogIcon, LanguagesIcon, ChevronDownIcon } from 'lucide-react'
import { useAuth } from '@/shared/auth/auth-context'
import { useLanguage } from '@/i18n/LanguageProvider'
import type { Language } from '@/i18n/config'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Separator } from '@/components/ui/separator'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'

function initials(name: string | undefined): string {
  if (!name) return '?'
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('')
}

function LanguageMenu() {
  const { t } = useTranslation()
  const { language, setLanguage } = useLanguage()

  const options: { value: Language; label: string }[] = [
    { value: 'en', label: t('nav.english') },
    { value: 'ar', label: t('nav.arabic') },
  ]

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm">
          <LanguagesIcon data-icon="inline-start" />
          {options.find((o) => o.value === language)?.label}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>{t('nav.language')}</DropdownMenuLabel>
        <DropdownMenuGroup>
          {options.map((option) => (
            <DropdownMenuItem key={option.value} onSelect={() => setLanguage(option.value)}>
              {option.label}
            </DropdownMenuItem>
          ))}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

export function AppLayout() {
  const { t } = useTranslation()
  const { user, logout, hasPermission } = useAuth()
  const navigate = useNavigate()

  const handleLogout = async () => {
    await logout()
    navigate('/login', { replace: true })
  }

  const navItems = [
    { to: '/users', label: t('nav.users'), icon: UsersIcon, show: hasPermission('identity.users.view') },
    { to: '/roles', label: t('nav.roles'), icon: ShieldCheckIcon, show: hasPermission('identity.roles.view') },
  ].filter((item) => item.show)

  return (
    <div className="flex h-screen bg-muted/30">
      <aside className="hidden w-64 flex-col border-e bg-background md:flex">
        <div className="flex h-14 items-center gap-2 px-5 font-heading text-base font-semibold">
          <ShieldCheckIcon className="size-5 text-primary" />
          {t('common.appName')}
        </div>
        <Separator />
        <nav className="flex flex-1 flex-col gap-1 p-3">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                  isActive
                    ? 'bg-primary text-primary-foreground'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                )
              }
            >
              <item.icon className="size-4" />
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>

      <div className="flex flex-1 flex-col overflow-hidden">
        <header className="flex h-14 items-center justify-between gap-3 border-b bg-background px-4 md:px-6">
          <div className="font-heading text-sm font-semibold md:hidden">{t('common.appName')}</div>
          <div className="ms-auto flex items-center gap-2">
            <LanguageMenu />
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-9 gap-2 px-1.5">
                  <Avatar className="size-7">
                    <AvatarFallback className="text-xs">{initials(user?.displayName)}</AvatarFallback>
                  </Avatar>
                  <span className="hidden text-sm font-medium sm:inline">{user?.displayName}</span>
                  <ChevronDownIcon className="size-4 text-muted-foreground" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-56">
                <DropdownMenuLabel className="flex flex-col gap-0.5">
                  <span className="text-sm font-medium text-foreground">{user?.displayName}</span>
                  <span className="text-xs font-normal text-muted-foreground">{user?.email}</span>
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuGroup>
                  <DropdownMenuItem onSelect={() => navigate('/account')}>
                    <UserCogIcon />
                    {t('nav.account')}
                  </DropdownMenuItem>
                </DropdownMenuGroup>
                <DropdownMenuSeparator />
                <DropdownMenuItem variant="destructive" onSelect={handleLogout}>
                  <LogOutIcon />
                  {t('nav.signOut')}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </header>
        <main className="flex-1 overflow-auto p-4 md:p-6">
          <div className="mx-auto w-full max-w-6xl">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}
