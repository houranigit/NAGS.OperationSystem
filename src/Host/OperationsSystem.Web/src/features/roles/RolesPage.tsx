import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { EllipsisVerticalIcon, KeyRoundIcon, PencilIcon, PlusIcon, ShieldCheckIcon, Trash2Icon } from 'lucide-react'
import { useAuth } from '@/shared/auth/auth-context'
import { toastError, toastSuccess } from '@/shared/toast'
import { PageHeader } from '@/shared/components/PageHeader'
import { Pagination } from '@/shared/components/Pagination'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
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
import type { RoleListItem } from '@/shared/api/types'
import { useDeleteRole, useRoles } from './api'
import { RoleFormDialog } from './RoleFormDialog'
import { PermissionsDialog } from './PermissionsDialog'

const PAGE_SIZE = 20

export function RolesPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()

  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const { data, isLoading } = useRoles({ page, pageSize: PAGE_SIZE, search })
  const deleteRole = useDeleteRole()

  const [formRole, setFormRole] = useState<RoleListItem | null>(null)
  const [showForm, setShowForm] = useState(false)
  const [permsRole, setPermsRole] = useState<RoleListItem | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<RoleListItem | null>(null)

  const canCreate = hasPermission('identity.roles.create')
  const canUpdate = hasPermission('identity.roles.update')
  const canManagePerms = hasPermission('identity.roles.manage-permissions')
  const canDelete = hasPermission('identity.roles.delete')

  const confirmDelete = async () => {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await deleteRole.mutateAsync(target.id)
      toastSuccess(t('roles.roleDeleted'))
    } catch (error) {
      toastError(error)
    }
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title={t('roles.title')}
        actions={
          canCreate ? (
            <Button
              onClick={() => {
                setFormRole(null)
                setShowForm(true)
              }}
            >
              <PlusIcon data-icon="inline-start" />
              {t('roles.new')}
            </Button>
          ) : undefined
        }
      />

      <Input
        placeholder={t('roles.searchPlaceholder')}
        value={search}
        onChange={(e) => {
          setSearch(e.target.value)
          setPage(1)
        }}
        className="h-8 max-w-xs"
      />

      <Card className="p-0">
        {isLoading ? (
          <div className="flex flex-col gap-2 p-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : !data || data.items.length === 0 ? (
          <Empty className="border-0">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <ShieldCheckIcon />
              </EmptyMedia>
              <EmptyTitle>{t('roles.empty')}</EmptyTitle>
              <EmptyDescription>{t('roles.emptyHint')}</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t('roles.columnName')}</TableHead>
                <TableHead>{t('roles.columnDescription')}</TableHead>
                <TableHead className="text-end">{t('roles.columnPermissions')}</TableHead>
                <TableHead className="text-end">{t('roles.columnUsers')}</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.items.map((role) => {
                const hasActions =
                  (canManagePerms || canUpdate || canDelete) && !role.isSystem
                return (
                  <TableRow key={role.id}>
                    <TableCell className="font-medium">
                      <div className="flex items-center gap-2">
                        {role.name}
                        {role.isSystem && <Badge variant="outline">{t('roles.system')}</Badge>}
                      </div>
                    </TableCell>
                    <TableCell className="text-muted-foreground">{role.description ?? t('common.none')}</TableCell>
                    <TableCell className="text-end text-muted-foreground">{role.permissionCount}</TableCell>
                    <TableCell className="text-end text-muted-foreground">{role.userCount}</TableCell>
                    <TableCell>
                      {hasActions && (
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="icon-sm" aria-label={t('common.actions')}>
                              <EllipsisVerticalIcon />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end" className="w-48">
                            <DropdownMenuGroup>
                              {canManagePerms && (
                                <DropdownMenuItem onSelect={() => setPermsRole(role)}>
                                  <KeyRoundIcon />
                                  {t('roles.managePermissions')}
                                </DropdownMenuItem>
                              )}
                              {canUpdate && (
                                <DropdownMenuItem
                                  onSelect={() => {
                                    setFormRole(role)
                                    setShowForm(true)
                                  }}
                                >
                                  <PencilIcon />
                                  {t('common.edit')}
                                </DropdownMenuItem>
                              )}
                            </DropdownMenuGroup>
                            {canDelete && (
                              <>
                                <DropdownMenuSeparator />
                                <DropdownMenuItem variant="destructive" onSelect={() => setDeleteTarget(role)}>
                                  <Trash2Icon />
                                  {t('common.delete')}
                                </DropdownMenuItem>
                              </>
                            )}
                          </DropdownMenuContent>
                        </DropdownMenu>
                      )}
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        )}
      </Card>

      {data && data.items.length > 0 && <Pagination result={data} onPageChange={setPage} />}

      {showForm && <RoleFormDialog role={formRole} onClose={() => setShowForm(false)} />}
      {permsRole && <PermissionsDialog role={permsRole} onClose={() => setPermsRole(null)} />}

      <AlertDialog open={!!deleteTarget} onOpenChange={(open) => !open && setDeleteTarget(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('roles.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('roles.deleteConfirm', { name: deleteTarget?.name })}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={confirmDelete}>
              {t('common.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
