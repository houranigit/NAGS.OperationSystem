import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { usePermissionCatalog, useRole, useUpdateRolePermissions } from './api'
import type { RoleListItem } from '@/shared/api/types'
import { toastSuccess } from '@/shared/toast'
import { extractApiError } from '@/shared/api/error'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { Checkbox } from '@/components/ui/checkbox'
import { Field, FieldLabel, FieldSet, FieldLegend, FieldGroup } from '@/components/ui/field'
import { Alert, AlertTitle } from '@/components/ui/alert'

function prettify(permission: string): string {
  const parts = permission.split('.')
  return (parts[parts.length - 1] ?? permission).replace(/-/g, ' ')
}

export function PermissionsDialog({ role, onClose }: { role: RoleListItem; onClose: () => void }) {
  const { t } = useTranslation()
  const catalog = usePermissionCatalog()
  const roleQuery = useRole(role.id)
  const updatePermissions = useUpdateRolePermissions()
  // `null` means "not edited yet" — fall back to the role's saved permissions.
  const [edited, setEdited] = useState<Set<string> | null>(null)
  const [serverError, setServerError] = useState<string | null>(null)

  const selected = edited ?? new Set(roleQuery.data?.permissions ?? [])

  const toggle = (permission: string) => {
    setEdited(() => {
      const next = new Set(selected)
      if (next.has(permission)) next.delete(permission)
      else next.add(permission)
      return next
    })
  }

  const save = async () => {
    setServerError(null)
    try {
      await updatePermissions.mutateAsync({ id: role.id, permissions: [...selected] })
      toastSuccess(t('roles.permissionsUpdated'))
      onClose()
    } catch (error) {
      setServerError(extractApiError(error, t('common.somethingWentWrong')))
    }
  }

  const loading = catalog.isLoading || roleQuery.isLoading

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t('roles.permissionsTitle', { name: role.name })}</DialogTitle>
          <DialogDescription>{t('roles.permissionsDescription')}</DialogDescription>
        </DialogHeader>

        {loading ? (
          <div className="flex justify-center py-8">
            <Spinner className="size-5 text-muted-foreground" />
          </div>
        ) : (
          <div className="flex max-h-[55vh] flex-col gap-5 overflow-auto pe-1">
            {catalog.data?.map((group) => (
              <FieldSet key={group.resource}>
                <FieldLegend variant="label" className="capitalize">
                  {group.resource}
                </FieldLegend>
                <FieldGroup className="gap-3">
                  {group.permissions.map((permission) => (
                    <Field key={permission} orientation="horizontal">
                      <Checkbox
                        id={permission}
                        checked={selected.has(permission)}
                        onCheckedChange={() => toggle(permission)}
                      />
                      <FieldLabel htmlFor={permission} className="font-normal capitalize">
                        {prettify(permission)}
                      </FieldLabel>
                    </Field>
                  ))}
                </FieldGroup>
              </FieldSet>
            ))}
            {serverError && (
              <Alert variant="destructive">
                <AlertTitle>{serverError}</AlertTitle>
              </Alert>
            )}
          </div>
        )}

        <DialogFooter className="flex-row items-center justify-between sm:justify-between">
          <span className="text-sm text-muted-foreground">{t('roles.selectedCount', { count: selected.size })}</span>
          <div className="flex gap-2">
            <Button variant="outline" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button onClick={save} disabled={updatePermissions.isPending || loading}>
              {updatePermissions.isPending && <Spinner data-icon="inline-start" />}
              {t('roles.savePermissions')}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
