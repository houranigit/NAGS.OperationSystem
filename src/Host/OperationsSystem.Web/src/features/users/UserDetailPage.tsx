import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
  EllipsisVerticalIcon,
  LockIcon,
  LockOpenIcon,
  MailIcon,
  PencilIcon,
  ShieldIcon,
  UserXIcon,
} from 'lucide-react'
import { useAuth } from '@/shared/auth/auth-context'
import { useLanguage } from '@/i18n/LanguageProvider'
import { formatDateTime } from '@/shared/format'
import { toastError, toastSuccess } from '@/shared/toast'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Separator } from '@/components/ui/separator'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Empty, EmptyHeader, EmptyTitle } from '@/components/ui/empty'
import {
  useDeactivateUser,
  useLockUser,
  useResendInvitation,
  useUnlockUser,
  useUser,
} from './api'
import { UserStatusBadge } from './UserStatusBadge'
import { EditUserDialog } from './EditUserDialog'
import { AssignRoleDialog } from './AssignRoleDialog'
import { useRevokeSession, useRevokeUserSessions, useUserSessions } from '@/features/sessions/api'
import { SessionsTable } from '@/features/sessions/SessionsTable'

function initials(name: string): string {
  return name.split(' ').filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join('')
}

function DetailRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5 py-2">
      <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{label}</dt>
      <dd className="text-sm">{value}</dd>
    </div>
  )
}

export function UserDetailPage() {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const { hasPermission } = useAuth()
  const { id } = useParams<{ id: string }>()

  const { data: user, isLoading } = useUser(id)
  const sessions = useUserSessions(hasPermission('identity.sessions.view') ? id : undefined)
  const revokeSession = useRevokeSession()
  const revokeAll = useRevokeUserSessions()

  const lockUser = useLockUser()
  const unlockUser = useUnlockUser()
  const deactivateUser = useDeactivateUser()
  const resendInvitation = useResendInvitation()

  const [showEdit, setShowEdit] = useState(false)
  const [showAssign, setShowAssign] = useState(false)
  const [showDeactivate, setShowDeactivate] = useState(false)
  const [showRevokeAll, setShowRevokeAll] = useState(false)

  const canUpdate = hasPermission('identity.users.update')
  const canAssign = hasPermission('identity.users.assign-role')
  const canLock = hasPermission('identity.users.lock')
  const canUnlock = hasPermission('identity.users.unlock')
  const canDeactivate = hasPermission('identity.users.deactivate')
  const canInvite = hasPermission('identity.users.invite')
  const canViewSessions = hasPermission('identity.sessions.view')
  const canRevokeSessions = hasPermission('identity.sessions.revoke')

  const run = async (fn: () => Promise<unknown>, message: string) => {
    try {
      await fn()
      toastSuccess(message)
    } catch (error) {
      toastError(error)
    }
  }

  const confirmDeactivate = async () => {
    if (!user) return
    setShowDeactivate(false)
    await run(() => deactivateUser.mutateAsync(user.id), t('users.userDeactivated'))
  }

  const confirmRevokeAll = async () => {
    if (!user) return
    setShowRevokeAll(false)
    await run(() => revokeAll.mutateAsync(user.id), t('sessions.sessionsRevoked'))
  }

  return (
    <div className="flex flex-col gap-5">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink asChild>
              <Link to="/users">{t('users.title')}</Link>
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{user?.displayName ?? t('users.detailTitle')}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      {isLoading ? (
        <Skeleton className="h-40 w-full" />
      ) : !user ? (
        <Empty>
          <EmptyHeader>
            <EmptyTitle>{t('users.empty')}</EmptyTitle>
          </EmptyHeader>
        </Empty>
      ) : (
        <>
          <Card>
            <CardHeader>
              <div className="flex items-center gap-3">
                <Avatar className="size-12">
                  <AvatarFallback>{initials(user.displayName)}</AvatarFallback>
                </Avatar>
                <div className="flex flex-col gap-1">
                  <CardTitle className="text-lg">{user.displayName}</CardTitle>
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    {user.email}
                  </div>
                  <UserStatusBadge status={user.status} isLockedOut={user.isLockedOut} />
                </div>
              </div>
              <CardAction>
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="outline" size="sm">
                      <EllipsisVerticalIcon data-icon="inline-start" />
                      {t('common.actions')}
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end" className="w-52">
                    <DropdownMenuGroup>
                      {canUpdate && (
                        <DropdownMenuItem onSelect={() => setShowEdit(true)}>
                          <PencilIcon />
                          {t('users.editProfile')}
                        </DropdownMenuItem>
                      )}
                      {canAssign && (
                        <DropdownMenuItem onSelect={() => setShowAssign(true)}>
                          <ShieldIcon />
                          {t('users.changeRole')}
                        </DropdownMenuItem>
                      )}
                      {canUnlock && user.isLockedOut && (
                        <DropdownMenuItem
                          onSelect={() => run(() => unlockUser.mutateAsync(user.id), t('users.userUnlocked'))}
                        >
                          <LockOpenIcon />
                          {t('users.unlock')}
                        </DropdownMenuItem>
                      )}
                      {canLock && !user.isLockedOut && user.status === 'Active' && (
                        <DropdownMenuItem
                          onSelect={() => run(() => lockUser.mutateAsync(user.id), t('users.userLocked'))}
                        >
                          <LockIcon />
                          {t('users.lock')}
                        </DropdownMenuItem>
                      )}
                      {canInvite && user.status === 'Invited' && (
                        <DropdownMenuItem
                          onSelect={() =>
                            run(() => resendInvitation.mutateAsync(user.id), t('users.invitationResent'))
                          }
                        >
                          <MailIcon />
                          {t('users.resendInvitation')}
                        </DropdownMenuItem>
                      )}
                    </DropdownMenuGroup>
                    {canDeactivate && user.status !== 'Deactivated' && (
                      <>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem variant="destructive" onSelect={() => setShowDeactivate(true)}>
                          <UserXIcon />
                          {t('users.deactivate')}
                        </DropdownMenuItem>
                      </>
                    )}
                  </DropdownMenuContent>
                </DropdownMenu>
              </CardAction>
            </CardHeader>
          </Card>

          <Tabs defaultValue="profile">
            <TabsList>
              <TabsTrigger value="profile">{t('users.tabProfile')}</TabsTrigger>
              {canViewSessions && <TabsTrigger value="sessions">{t('users.tabSessions')}</TabsTrigger>}
            </TabsList>

            <TabsContent value="profile">
              <Card>
                <CardContent>
                  <dl className="divide-y">
                    <DetailRow label={t('users.columnEmail')} value={user.email} />
                    <DetailRow label={t('users.columnRole')} value={user.roleName} />
                    <DetailRow
                      label={t('users.columnStatus')}
                      value={<UserStatusBadge status={user.status} isLockedOut={user.isLockedOut} />}
                    />
                    <DetailRow
                      label={t('users.createdAt')}
                      value={formatDateTime(user.createdAtUtc, language) ?? t('common.none')}
                    />
                    <DetailRow
                      label={t('users.columnLastLogin')}
                      value={formatDateTime(user.lastLoginAtUtc, language) ?? t('common.never')}
                    />
                  </dl>
                </CardContent>
              </Card>
            </TabsContent>

            {canViewSessions && (
              <TabsContent value="sessions">
                <Card>
                  <CardHeader>
                    <CardTitle>{t('sessions.title')}</CardTitle>
                    {canRevokeSessions && (
                      <CardAction>
                        <Button variant="outline" size="sm" onClick={() => setShowRevokeAll(true)}>
                          {t('sessions.revokeAll')}
                        </Button>
                      </CardAction>
                    )}
                  </CardHeader>
                  <Separator />
                  <CardContent className="px-0">
                    <SessionsTable
                      sessions={sessions.data}
                      isLoading={sessions.isLoading}
                      onRevoke={canRevokeSessions ? (sid) => revokeSession.mutateAsync(sid) : undefined}
                    />
                  </CardContent>
                </Card>
              </TabsContent>
            )}
          </Tabs>

          {showEdit && <EditUserDialog user={user} onClose={() => setShowEdit(false)} />}
          {showAssign && <AssignRoleDialog user={user} onClose={() => setShowAssign(false)} />}

          <AlertDialog open={showDeactivate} onOpenChange={setShowDeactivate}>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{t('users.deactivateTitle')}</AlertDialogTitle>
                <AlertDialogDescription>
                  {t('users.deactivateConfirm', { name: user.displayName })}
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
                <AlertDialogAction variant="destructive" onClick={confirmDeactivate}>
                  {t('users.deactivate')}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>

          <AlertDialog open={showRevokeAll} onOpenChange={setShowRevokeAll}>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{t('sessions.revokeAllTitle')}</AlertDialogTitle>
                <AlertDialogDescription>{t('sessions.revokeAllConfirm')}</AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
                <AlertDialogAction variant="destructive" onClick={confirmRevokeAll}>
                  {t('sessions.revokeAll')}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </>
      )}
    </div>
  )
}
