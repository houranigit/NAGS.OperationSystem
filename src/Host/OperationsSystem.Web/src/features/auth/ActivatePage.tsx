import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { CircleCheckIcon, ShieldCheckIcon } from 'lucide-react'
import { authApi } from './api'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Alert, AlertTitle } from '@/components/ui/alert'
import { extractApiError } from '@/shared/api/error'

const schema = z
  .object({
    email: z.string().min(1, 'auth.emailRequired').email('auth.emailInvalid'),
    invitationToken: z.string().min(1, 'auth.tokenRequired'),
    newPassword: z.string().min(8, 'auth.passwordMinLength'),
    confirmPassword: z.string().min(1, 'auth.passwordRequired'),
  })
  .refine((v) => v.newPassword === v.confirmPassword, {
    message: 'auth.passwordsDontMatch',
    path: ['confirmPassword'],
  })

type FormValues = z.infer<typeof schema>

export function ActivatePage() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const [serverError, setServerError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: params.get('email') ?? '',
      invitationToken: params.get('token') ?? '',
      newPassword: '',
      confirmPassword: '',
    },
  })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      await authApi.activate(values.email, values.invitationToken, values.newPassword)
      setDone(true)
    } catch (error) {
      setServerError(extractApiError(error, t('common.somethingWentWrong')))
    }
  })

  return (
    <div className="flex min-h-screen items-center justify-center bg-muted/30 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader className="text-center">
          <div className="mx-auto mb-2 flex size-11 items-center justify-center rounded-xl bg-primary text-primary-foreground">
            {done ? <CircleCheckIcon className="size-5" /> : <ShieldCheckIcon className="size-5" />}
          </div>
          <CardTitle>{t('auth.activateTitle')}</CardTitle>
          <CardDescription>{done ? t('auth.activated') : t('auth.activateSubtitle')}</CardDescription>
        </CardHeader>
        <CardContent>
          {done ? (
            <Button asChild className="w-full">
              <Link to="/login">{t('auth.goToSignIn')}</Link>
            </Button>
          ) : (
            <form onSubmit={onSubmit} noValidate>
              <FieldGroup>
                <Field data-invalid={!!errors.email}>
                  <FieldLabel htmlFor="email">{t('auth.email')}</FieldLabel>
                  <Input id="email" type="email" aria-invalid={!!errors.email} {...register('email')} />
                  <FieldError>{errors.email && t(errors.email.message!)}</FieldError>
                </Field>
                <Field data-invalid={!!errors.invitationToken}>
                  <FieldLabel htmlFor="invitationToken">{t('auth.invitationToken')}</FieldLabel>
                  <Input id="invitationToken" aria-invalid={!!errors.invitationToken} {...register('invitationToken')} />
                  <FieldError>{errors.invitationToken && t(errors.invitationToken.message!)}</FieldError>
                </Field>
                <Field data-invalid={!!errors.newPassword}>
                  <FieldLabel htmlFor="newPassword">{t('auth.newPassword')}</FieldLabel>
                  <Input
                    id="newPassword"
                    type="password"
                    autoComplete="new-password"
                    aria-invalid={!!errors.newPassword}
                    {...register('newPassword')}
                  />
                  <FieldError>{errors.newPassword && t(errors.newPassword.message!)}</FieldError>
                </Field>
                <Field data-invalid={!!errors.confirmPassword}>
                  <FieldLabel htmlFor="confirmPassword">{t('auth.confirmPassword')}</FieldLabel>
                  <Input
                    id="confirmPassword"
                    type="password"
                    autoComplete="new-password"
                    aria-invalid={!!errors.confirmPassword}
                    {...register('confirmPassword')}
                  />
                  <FieldError>{errors.confirmPassword && t(errors.confirmPassword.message!)}</FieldError>
                </Field>
                {serverError && (
                  <Alert variant="destructive">
                    <AlertTitle>{serverError}</AlertTitle>
                  </Alert>
                )}
                <Button type="submit" className="w-full" disabled={isSubmitting}>
                  {isSubmitting && <Spinner data-icon="inline-start" />}
                  {isSubmitting ? t('auth.activating') : t('auth.activate')}
                </Button>
              </FieldGroup>
            </form>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
