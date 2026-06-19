import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MonitorIcon, XIcon } from 'lucide-react'
import { useLanguage } from '@/i18n/LanguageProvider'
import { formatDateTime } from '@/shared/format'
import { toastError, toastSuccess } from '@/shared/toast'
import type { UserSession } from '@/shared/api/types'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Empty, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
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

function describeDevice(userAgent: string | null, fallback: string): string {
  if (!userAgent) return fallback
  const browser =
    /Edg/.test(userAgent) ? 'Edge'
    : /Chrome/.test(userAgent) ? 'Chrome'
    : /Firefox/.test(userAgent) ? 'Firefox'
    : /Safari/.test(userAgent) ? 'Safari'
    : null
  const os =
    /Windows/.test(userAgent) ? 'Windows'
    : /Mac OS X|Macintosh/.test(userAgent) ? 'macOS'
    : /Android/.test(userAgent) ? 'Android'
    : /iPhone|iPad|iOS/.test(userAgent) ? 'iOS'
    : /Linux/.test(userAgent) ? 'Linux'
    : null
  if (browser && os) return `${browser} · ${os}`
  return browser ?? os ?? fallback
}

function SessionStatus({ session }: { session: UserSession }) {
  const { t } = useTranslation()
  if (session.isActive) return <Badge variant="default">{t('sessions.active')}</Badge>
  if (session.revokedAtUtc) return <Badge variant="outline">{t('sessions.revoked')}</Badge>
  return <Badge variant="secondary">{t('sessions.expired')}</Badge>
}

export function SessionsTable({
  sessions,
  isLoading,
  onRevoke,
}: {
  sessions: UserSession[] | undefined
  isLoading: boolean
  onRevoke?: (sessionId: string) => Promise<unknown>
}) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const [target, setTarget] = useState<UserSession | null>(null)

  const confirmRevoke = async () => {
    if (!target || !onRevoke) return
    const id = target.id
    setTarget(null)
    try {
      await onRevoke(id)
      toastSuccess(t('sessions.sessionRevoked'))
    } catch (error) {
      toastError(error)
    }
  }

  if (isLoading) {
    return (
      <div className="flex flex-col gap-2">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-10 w-full" />
        ))}
      </div>
    )
  }

  if (!sessions || sessions.length === 0) {
    return (
      <Empty className="border-0">
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <MonitorIcon />
          </EmptyMedia>
          <EmptyTitle>{t('sessions.empty')}</EmptyTitle>
        </EmptyHeader>
      </Empty>
    )
  }

  return (
    <>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{t('sessions.columnDevice')}</TableHead>
            <TableHead>{t('sessions.columnIp')}</TableHead>
            <TableHead>{t('sessions.columnCreated')}</TableHead>
            <TableHead>{t('sessions.columnStatus')}</TableHead>
            {onRevoke && <TableHead className="w-10" />}
          </TableRow>
        </TableHeader>
        <TableBody>
          {sessions.map((session) => (
            <TableRow key={session.id}>
              <TableCell className="font-medium">
                <div className="flex items-center gap-2">
                  {describeDevice(session.userAgent, t('sessions.unknownDevice'))}
                  {session.isCurrent && <Badge variant="secondary">{t('sessions.current')}</Badge>}
                </div>
              </TableCell>
              <TableCell className="text-muted-foreground">{session.createdByIp ?? t('common.none')}</TableCell>
              <TableCell className="text-muted-foreground">
                {formatDateTime(session.createdAtUtc, language) ?? t('common.none')}
              </TableCell>
              <TableCell>
                <SessionStatus session={session} />
              </TableCell>
              {onRevoke && (
                <TableCell>
                  {session.isActive && !session.isCurrent && (
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={t('sessions.revoke')}
                      onClick={() => setTarget(session)}
                    >
                      <XIcon />
                    </Button>
                  )}
                </TableCell>
              )}
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <AlertDialog open={!!target} onOpenChange={(open) => !open && setTarget(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('sessions.revokeTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('sessions.revokeConfirm')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={confirmRevoke}>
              {t('sessions.revoke')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
