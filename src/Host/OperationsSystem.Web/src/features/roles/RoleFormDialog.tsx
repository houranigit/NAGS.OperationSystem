import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useCreateRole, useUpdateRole } from './api'
import type { RoleListItem } from '@/shared/api/types'
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
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Alert, AlertTitle } from '@/components/ui/alert'

const schema = z.object({
  name: z.string().min(1, 'roles.nameRequired').max(100),
  description: z.string().max(500).optional(),
})

type FormValues = z.infer<typeof schema>

export function RoleFormDialog({ role, onClose }: { role: RoleListItem | null; onClose: () => void }) {
  const { t } = useTranslation()
  const isEdit = !!role
  const createRole = useCreateRole()
  const updateRole = useUpdateRole()
  const [serverError, setServerError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: role?.name ?? '', description: role?.description ?? '' },
  })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      if (isEdit && role) {
        await updateRole.mutateAsync({ id: role.id, name: values.name, description: values.description || null })
        toastSuccess(t('roles.roleUpdated'))
      } else {
        await createRole.mutateAsync({ name: values.name, description: values.description || null, permissions: [] })
        toastSuccess(t('roles.roleCreated'))
      }
      onClose()
    } catch (error) {
      setServerError(extractApiError(error, t('common.somethingWentWrong')))
    }
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{isEdit ? t('roles.editTitle') : t('roles.newTitle')}</DialogTitle>
        </DialogHeader>
        <form id="role-form" onSubmit={onSubmit} noValidate>
          <FieldGroup>
            <Field data-invalid={!!errors.name}>
              <FieldLabel htmlFor="name">{t('roles.name')}</FieldLabel>
              <Input id="name" aria-invalid={!!errors.name} {...register('name')} />
              <FieldError>{errors.name && t(errors.name.message!)}</FieldError>
            </Field>
            <Field>
              <FieldLabel htmlFor="description">{t('roles.description')}</FieldLabel>
              <Input id="description" {...register('description')} />
            </Field>
            {serverError && (
              <Alert variant="destructive">
                <AlertTitle>{serverError}</AlertTitle>
              </Alert>
            )}
          </FieldGroup>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" form="role-form" disabled={isSubmitting}>
            {isSubmitting && <Spinner data-icon="inline-start" />}
            {t('common.save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
