import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import type { UserStatus } from '@/shared/api/types'

const statusVariant: Record<UserStatus, 'default' | 'secondary' | 'outline'> = {
  Active: 'default',
  Invited: 'secondary',
  Deactivated: 'outline',
}

const statusLabelKey: Record<UserStatus, string> = {
  Active: 'users.statusActive',
  Invited: 'users.statusInvited',
  Deactivated: 'users.statusDeactivated',
}

export function UserStatusBadge({ status, isLockedOut }: { status: UserStatus; isLockedOut: boolean }) {
  const { t } = useTranslation()
  return (
    <div className="flex items-center gap-1.5">
      <Badge variant={statusVariant[status]}>{t(statusLabelKey[status])}</Badge>
      {isLockedOut && <Badge variant="destructive">{t('users.locked')}</Badge>}
    </div>
  )
}
