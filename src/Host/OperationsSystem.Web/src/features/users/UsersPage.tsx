import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
  EllipsisVerticalIcon,
  EyeIcon,
  LockIcon,
  LockOpenIcon,
  MailIcon,
  ShieldIcon,
  UserPlusIcon,
  UserXIcon,
} from 'lucide-react'
import { useAuth } from '@/shared/auth/auth-context'
import { useLanguage } from '@/i18n/LanguageProvider'
import { formatDateTime } from '@/shared/format'
import { toastError, toastSuccess } from '@/shared/toast'
import { PageHeader } from '@/shared/components/PageHeader'
import { Pagination } from '@/shared/components/Pagination'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
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
import type { UserListItem, UserStatus } from '@/shared/api/types'
import {
  useDeactivateUser,
  useLockUser,
  useResendInvitation,
  useUnlockUser,
  useUsers,
} from './api'
import { InviteUserDialog } from './InviteUserDialog'
import { AssignRoleDialog } from './AssignRoleDialog'
import { UserStatusBadge } from './UserStatusBadge'

const ALL_STATUSES = 'all'
const PAGE_SIZE = 20

export function UsersPage() {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()

  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<UserStatus | ''>('')
  const { data, isLoading } = useUsers({ page, pageSize: PAGE_SIZE, search, status })

  const [showInvite, setShowInvite] = useState(false)
  const [assignUser, setAssignUser] = useState<UserListItem | null>(null)
  const [deactivateTarget, setDeactivateTarget] = useState<UserListItem | null>(null)

  const lockUser = useLockUser()
  const unlockUser = useUnlockUser()
  const deactivateUser = useDeactivateUser()
  const resendInvitation = useResendInvitation()

  const canInvite = hasPermission('identity.users.invite')
  const canAssign = hasPermission('identity.users.assign-role')
  const canLock = hasPermission('identity.users.lock')
  const canUnlock = hasPermission('identity.users.unlock')
  const canDeactivate = hasPermission('identity.users.deactivate')

  const resetPage = () => setPage(1)

  const run = async (fn: () => Promise<unknown>, successMessage: string) => {
    try {
      await fn()
      toastSuccess(successMessage)
    } catch (error) {
      toastError(error)
    }
  }

  const confirmDeactivate = async () => {
    if (!deactivateTarget) return
    const target = deactivateTarget
    setDeactivateTarget(null)
    await run(() => deactivateUser.mutateAsync(target.id), t('users.userDeactivated'))
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title={t('users.title')}
        actions={
          canInvite ? (
            <Button onClick={() => setShowInvite(true)}>
              <UserPlusIcon data-icon="inline-start" />
              {t('users.invite')}
            </Button>
          ) : undefined
        }
      />

      <div className="flex flex-wrap gap-2">
        <Input
          placeholder={t('users.searchPlaceholder')}
          value={search}
          onChange={(e) => {
            setSearch(e.target.value)
            resetPage()
          }}
          className="h-8 max-w-xs"
        />
        <Select
          value={status === '' ? ALL_STATUSES : status}
          onValueChange={(value) => {
            setStatus(value === ALL_STATUSES ? '' : (value as UserStatus))
            resetPage()
          }}
        >
          <SelectTrigger className="w-44">
            <SelectValue placeholder={t('users.allStatuses')} />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value={ALL_STATUSES}>{t('users.allStatuses')}</SelectItem>
              <SelectItem value="Active">{t('users.statusActive')}</SelectItem>
              <SelectItem value="Invited">{t('users.statusInvited')}</SelectItem>
              <SelectItem value="Deactivated">{t('users.statusDeactivated')}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </div>

      <Card className="p-0">
        {isLoading ? (
          <div className="flex flex-col gap-2 p-4">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : !data || data.items.length === 0 ? (
          <Empty className="border-0">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <UserPlusIcon />
              </EmptyMedia>
              <EmptyTitle>{t('users.empty')}</EmptyTitle>
              <EmptyDescription>{t('users.emptyHint')}</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t('users.columnName')}</TableHead>
                <TableHead>{t('users.columnEmail')}</TableHead>
                <TableHead>{t('users.columnRole')}</TableHead>
                <TableHead>{t('users.columnStatus')}</TableHead>
                <TableHead>{t('users.columnLastLogin')}</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.items.map((user) => (
                <TableRow
                  key={user.id}
                  className="cursor-pointer"
                  onClick={() => navigate(`/users/${user.id}`)}
                >
                  <TableCell className="font-medium">{user.displayName}</TableCell>
                  <TableCell className="text-muted-foreground">{user.email}</TableCell>
                  <TableCell className="text-muted-foreground">{user.roleName}</TableCell>
                  <TableCell>
                    <UserStatusBadge status={user.status} isLockedOut={user.isLockedOut} />
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {formatDateTime(user.lastLoginAtUtc, language) ?? t('common.never')}
                  </TableCell>
                  <TableCell onClick={(e) => e.stopPropagation()}>
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon-sm" aria-label={t('common.actions')}>
                          <EllipsisVerticalIcon />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end" className="w-48">
                        <DropdownMenuGroup>
                          <DropdownMenuItem onSelect={() => navigate(`/users/${user.id}`)}>
                            <EyeIcon />
                            {t('users.view')}
                          </DropdownMenuItem>
                          {canAssign && (
                            <DropdownMenuItem onSelect={() => setAssignUser(user)}>
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
                            <DropdownMenuItem variant="destructive" onSelect={() => setDeactivateTarget(user)}>
                              <UserXIcon />
                              {t('users.deactivate')}
                            </DropdownMenuItem>
                          </>
                        )}
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      {data && data.items.length > 0 && <Pagination result={data} onPageChange={setPage} />}

      {showInvite && <InviteUserDialog onClose={() => setShowInvite(false)} />}
      {assignUser && <AssignRoleDialog user={assignUser} onClose={() => setAssignUser(null)} />}

      <AlertDialog open={!!deactivateTarget} onOpenChange={(open) => !open && setDeactivateTarget(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('users.deactivateTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('users.deactivateConfirm', { name: deactivateTarget?.displayName })}
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
    </div>
  )
}
