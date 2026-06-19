import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useUpdateUser } from './api'
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
  displayName: z.string().min(1, 'users.displayNameRequired').max(150),
})

type FormValues = z.infer<typeof schema>

export function EditUserDialog({
  user,
  onClose,
}: {
  user: { id: string; displayName: string }
  onClose: () => void
}) {
  const { t } = useTranslation()
  const updateUser = useUpdateUser()
  const [serverError, setServerError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { displayName: user.displayName } })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      await updateUser.mutateAsync({ id: user.id, displayName: values.displayName })
      toastSuccess(t('users.userUpdated'))
      onClose()
    } catch (error) {
      setServerError(extractApiError(error, t('common.somethingWentWrong')))
    }
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t('users.editTitle')}</DialogTitle>
        </DialogHeader>
        <form id="edit-user-form" onSubmit={onSubmit} noValidate>
          <FieldGroup>
            <Field data-invalid={!!errors.displayName}>
              <FieldLabel htmlFor="displayName">{t('users.displayName')}</FieldLabel>
              <Input id="displayName" aria-invalid={!!errors.displayName} {...register('displayName')} />
              <FieldError>{errors.displayName && t(errors.displayName.message!)}</FieldError>
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
          <Button type="submit" form="edit-user-form" disabled={isSubmitting}>
            {isSubmitting && <Spinner data-icon="inline-start" />}
            {t('common.save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
