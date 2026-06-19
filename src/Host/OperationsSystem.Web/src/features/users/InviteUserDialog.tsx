import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useInviteUser } from './api'
import { useAllRoles } from '@/features/roles/api'
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
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Alert, AlertTitle } from '@/components/ui/alert'
import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

const schema = z.object({
  email: z.string().min(1, 'auth.emailRequired').email('auth.emailInvalid'),
  displayName: z.string().min(1, 'users.displayNameRequired').max(150),
  roleId: z.string().min(1, 'users.roleRequired'),
})

type FormValues = z.infer<typeof schema>

export function InviteUserDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const roles = useAllRoles()
  const inviteUser = useInviteUser()
  const [serverError, setServerError] = useState<string | null>(null)
  const [invitationToken, setInvitationToken] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '', displayName: '', roleId: '' } })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      const result = await inviteUser.mutateAsync(values)
      setInvitationToken(result.invitationToken)
      toastSuccess(t('users.userInvited'))
    } catch (error) {
      setServerError(extractApiError(error, t('common.somethingWentWrong')))
    }
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t('users.inviteTitle')}</DialogTitle>
          <DialogDescription>{t('users.inviteDescription')}</DialogDescription>
        </DialogHeader>

        {invitationToken ? (
          <div className="flex flex-col gap-2">
            <p className="text-sm text-muted-foreground">{t('users.invitationCreated')}</p>
            <code className="block rounded-md bg-muted p-2 text-xs break-all">{invitationToken}</code>
          </div>
        ) : (
          <form id="invite-form" onSubmit={onSubmit} noValidate>
            <FieldGroup>
              <Field data-invalid={!!errors.email}>
                <FieldLabel htmlFor="email">{t('users.columnEmail')}</FieldLabel>
                <Input id="email" type="email" aria-invalid={!!errors.email} {...register('email')} />
                <FieldError>{errors.email && t(errors.email.message!)}</FieldError>
              </Field>
              <Field data-invalid={!!errors.displayName}>
                <FieldLabel htmlFor="displayName">{t('users.displayName')}</FieldLabel>
                <Input id="displayName" aria-invalid={!!errors.displayName} {...register('displayName')} />
                <FieldError>{errors.displayName && t(errors.displayName.message!)}</FieldError>
              </Field>
              <Field data-invalid={!!errors.roleId}>
                <FieldLabel htmlFor="roleId">{t('users.role')}</FieldLabel>
                <Controller
                  control={control}
                  name="roleId"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger id="roleId" aria-invalid={!!errors.roleId} className="w-full">
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
                  )}
                />
                <FieldError>{errors.roleId && t(errors.roleId.message!)}</FieldError>
              </Field>
              {serverError && (
                <Alert variant="destructive">
                  <AlertTitle>{serverError}</AlertTitle>
                </Alert>
              )}
            </FieldGroup>
          </form>
        )}

        <DialogFooter>
          {invitationToken ? (
            <Button onClick={onClose}>{t('common.done')}</Button>
          ) : (
            <>
              <Button variant="outline" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button type="submit" form="invite-form" disabled={isSubmitting}>
                {isSubmitting && <Spinner data-icon="inline-start" />}
                {isSubmitting ? t('users.inviting') : t('users.sendInvitation')}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
