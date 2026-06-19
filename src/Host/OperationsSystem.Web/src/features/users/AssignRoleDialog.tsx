import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAssignRole } from './api'
import { useAllRoles } from '@/features/roles/api'
import { toastSuccess } from '@/shared/toast'
import { extractApiError } from '@/shared/api/error'
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { Field, FieldLabel } from '@/components/ui/field'
import { Alert, AlertTitle } from '@/components/ui/alert'
import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

interface AssignRoleTarget {
  id: string
  displayName: string
  roleId: string
}

export function AssignRoleDialog({ user, onClose }: { user: AssignRoleTarget; onClose: () => void }) {
  const { t } = useTranslation()
  const roles = useAllRoles()
  const assignRole = useAssignRole()
  const [roleId, setRoleId] = useState(user.roleId)
  const [serverError, setServerError] = useState<string | null>(null)

  const save = async () => {
    setServerError(null)
    try {
      await assignRole.mutateAsync({ id: user.id, roleId })
      toastSuccess(t('users.roleAssigned'))
      onClose()
    } catch (error) {
      setServerError(extractApiError(error, t('common.somethingWentWrong')))
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t('users.changeRoleTitle', { name: user.displayName })}</DialogTitle>
        </DialogHeader>
        <Field>
          <FieldLabel htmlFor="role">{t('users.role')}</FieldLabel>
          <Select value={roleId} onValueChange={setRoleId}>
            <SelectTrigger id="role" className="w-full">
              <SelectValue placeholder={t('users.selectRole')} />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                {roles.data?.map((role) => (
                  <SelectItem key={role.id} value={role.id}>
                    {role.name}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        {serverError && (
          <Alert variant="destructive">
            <AlertTitle>{serverError}</AlertTitle>
          </Alert>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button onClick={save} disabled={assignRole.isPending}>
            {assignRole.isPending && <Spinner data-icon="inline-start" />}
            {t('common.save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
