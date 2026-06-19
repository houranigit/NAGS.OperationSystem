import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '@/shared/auth/auth-context'
import { authApi } from '@/features/auth/api'
import { toastError, toastSuccess } from '@/shared/toast'
import { extractApiError } from '@/shared/api/error'
import { PageHeader } from '@/shared/components/PageHeader'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { Separator } from '@/components/ui/separator'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Alert, AlertTitle } from '@/components/ui/alert'
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
import {
  useMySessions,
  useRevokeMyOtherSessions,
  useRevokeMySession,
} from '@/features/sessions/api'
import { SessionsTable } from '@/features/sessions/SessionsTable'

const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, 'account.currentPasswordRequired'),
    newPassword: z.string().min(8, 'auth.passwordMinLength'),
    confirmPassword: z.string().min(1, 'auth.passwordRequired'),
  })
  .refine((v) => v.newPassword === v.confirmPassword, {
    message: 'auth.passwordsDontMatch',
    path: ['confirmPassword'],
  })

type PasswordValues = z.infer<typeof passwordSchema>

function ProfileTab() {
  const { t } = useTranslation()
  const { user } = useAuth()
  return (
    <Card>
      <CardContent>
        <dl className="divide-y">
          <div className="flex flex-col gap-0.5 py-2">
            <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
              {t('users.displayName')}
            </dt>
            <dd className="text-sm">{user?.displayName}</dd>
          </div>
          <div className="flex flex-col gap-0.5 py-2">
            <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
              {t('users.columnEmail')}
            </dt>
            <dd className="text-sm">{user?.email}</dd>
          </div>
          <div className="flex flex-col gap-0.5 py-2">
            <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
              {t('users.columnRole')}
            </dt>
            <dd className="text-sm">{user?.roleName}</dd>
          </div>
        </dl>
      </CardContent>
    </Card>
  )
}

function SecurityTab() {
  const { t } = useTranslation()
  const [serverError, setServerError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<PasswordValues>({
    resolver: zodResolver(passwordSchema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      await authApi.changePassword(values.currentPassword, values.newPassword)
      toastSuccess(t('account.passwordUpdated'))
      reset()
    } catch (error) {
      setServerError(extractApiError(error, t('common.somethingWentWrong')))
    }
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('account.changePassword')}</CardTitle>
      </CardHeader>
      <Separator />
      <CardContent>
        <form onSubmit={onSubmit} noValidate className="max-w-sm">
          <FieldGroup>
            <Field data-invalid={!!errors.currentPassword}>
              <FieldLabel htmlFor="currentPassword">{t('account.currentPassword')}</FieldLabel>
              <Input
                id="currentPassword"
                type="password"
                autoComplete="current-password"
                aria-invalid={!!errors.currentPassword}
                {...register('currentPassword')}
              />
              <FieldError>{errors.currentPassword && t(errors.currentPassword.message!)}</FieldError>
            </Field>
            <Field data-invalid={!!errors.newPassword}>
              <FieldLabel htmlFor="newPassword">{t('account.newPassword')}</FieldLabel>
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
              <FieldLabel htmlFor="confirmPassword">{t('account.confirmPassword')}</FieldLabel>
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
            <Button type="submit" disabled={isSubmitting} className="w-fit">
              {isSubmitting && <Spinner data-icon="inline-start" />}
              {t('account.updatePassword')}
            </Button>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  )
}

function SessionsTab() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const sessions = useMySessions()
  const revokeMySession = useRevokeMySession()
  const revokeOthers = useRevokeMyOtherSessions()
  const [showRevokeOthers, setShowRevokeOthers] = useState(false)

  const hasOtherActive = (sessions.data ?? []).some((s) => s.isActive && !s.isCurrent)

  const confirmRevokeOthers = async () => {
    setShowRevokeOthers(false)
    try {
      await revokeOthers.mutateAsync()
      await queryClient.invalidateQueries({ queryKey: ['my-sessions'] })
      toastSuccess(t('sessions.othersSignedOut'))
    } catch (error) {
      toastError(error)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('sessions.mine')}</CardTitle>
        <CardDescription>{t('sessions.description')}</CardDescription>
        {hasOtherActive && (
          <CardAction>
            <Button variant="outline" size="sm" onClick={() => setShowRevokeOthers(true)}>
              {t('sessions.signOutOthers')}
            </Button>
          </CardAction>
        )}
      </CardHeader>
      <Separator />
      <CardContent className="px-0">
        <SessionsTable
          sessions={sessions.data}
          isLoading={sessions.isLoading}
          onRevoke={(sid) => revokeMySession.mutateAsync(sid)}
        />
      </CardContent>

      <AlertDialog open={showRevokeOthers} onOpenChange={setShowRevokeOthers}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('sessions.signOutOthersTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('sessions.signOutOthersConfirm')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={confirmRevokeOthers}>
              {t('sessions.signOutOthers')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  )
}

export function AccountPage() {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-5">
      <PageHeader title={t('account.title')} />
      <Tabs defaultValue="profile">
        <TabsList>
          <TabsTrigger value="profile">{t('account.profile')}</TabsTrigger>
          <TabsTrigger value="security">{t('account.security')}</TabsTrigger>
          <TabsTrigger value="sessions">{t('account.sessions')}</TabsTrigger>
        </TabsList>
        <TabsContent value="profile">
          <ProfileTab />
        </TabsContent>
        <TabsContent value="security">
          <SecurityTab />
        </TabsContent>
        <TabsContent value="sessions">
          <SessionsTab />
        </TabsContent>
      </Tabs>
    </div>
  )
}
