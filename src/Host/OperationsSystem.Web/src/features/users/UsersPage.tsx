import { useState } from 'react'
import { Badge, Button, Card, EmptyState, Input, Select, Spinner } from '@/shared/ui'
import { useAuth } from '@/shared/auth/auth-context'
import {
  useDeactivateUser,
  useLockUser,
  useResendInvitation,
  useUnlockUser,
  useUsers,
} from './api'
import { InviteUserDialog } from './InviteUserDialog'
import { AssignRoleDialog } from './AssignRoleDialog'
import type { UserListItem, UserStatus } from '@/shared/api/types'
import { extractApiError } from '@/shared/api/error'

function statusTone(status: UserStatus): 'green' | 'amber' | 'slate' {
  if (status === 'Active') return 'green'
  if (status === 'Invited') return 'amber'
  return 'slate'
}

export function UsersPage() {
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<UserStatus | ''>('')
  const { data, isLoading } = useUsers(search, status)

  const [showInvite, setShowInvite] = useState(false)
  const [assignUser, setAssignUser] = useState<UserListItem | null>(null)

  const lockUser = useLockUser()
  const unlockUser = useUnlockUser()
  const deactivateUser = useDeactivateUser()
  const resendInvitation = useResendInvitation()

  const canInvite = hasPermission('identity.users.invite')
  const canAssign = hasPermission('identity.users.assign-role')
  const canLock = hasPermission('identity.users.lock')
  const canUnlock = hasPermission('identity.users.unlock')
  const canDeactivate = hasPermission('identity.users.deactivate')

  const run = async (fn: () => Promise<unknown>) => {
    try {
      await fn()
    } catch (error) {
      alert(extractApiError(error))
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-slate-900">Users</h1>
        {canInvite && <Button onClick={() => setShowInvite(true)}>Invite user</Button>}
      </div>

      <div className="flex gap-2">
        <Input placeholder="Search users…" value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />
        <Select value={status} onChange={(e) => setStatus(e.target.value as UserStatus | '')} className="max-w-[160px]">
          <option value="">All statuses</option>
          <option value="Active">Active</option>
          <option value="Invited">Invited</option>
          <option value="Deactivated">Deactivated</option>
        </Select>
      </div>

      <Card>
        {isLoading ? (
          <div className="flex justify-center py-12">
            <Spinner />
          </div>
        ) : !data || data.items.length === 0 ? (
          <EmptyState message="No users found." />
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
                <th className="px-4 py-3 font-medium">Name</th>
                <th className="px-4 py-3 font-medium">Email</th>
                <th className="px-4 py-3 font-medium">Role</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {data.items.map((user) => (
                <tr key={user.id} className="border-b border-slate-100 last:border-0">
                  <td className="px-4 py-3 font-medium text-slate-900">{user.displayName}</td>
                  <td className="px-4 py-3 text-slate-600">{user.email}</td>
                  <td className="px-4 py-3 text-slate-600">{user.roleName}</td>
                  <td className="px-4 py-3">
                    <div className="flex gap-1">
                      <Badge tone={statusTone(user.status)}>{user.status}</Badge>
                      {user.isLockedOut && <Badge tone="red">locked</Badge>}
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex flex-wrap justify-end gap-1">
                      {canAssign && (
                        <Button variant="ghost" onClick={() => setAssignUser(user)}>
                          Role
                        </Button>
                      )}
                      {canUnlock && user.isLockedOut && (
                        <Button variant="ghost" onClick={() => run(() => unlockUser.mutateAsync(user.id))}>
                          Unlock
                        </Button>
                      )}
                      {canLock && !user.isLockedOut && user.status === 'Active' && (
                        <Button variant="ghost" onClick={() => run(() => lockUser.mutateAsync(user.id))}>
                          Lock
                        </Button>
                      )}
                      {canInvite && user.status === 'Invited' && (
                        <Button variant="ghost" onClick={() => run(() => resendInvitation.mutateAsync(user.id))}>
                          Resend
                        </Button>
                      )}
                      {canDeactivate && user.status !== 'Deactivated' && (
                        <Button variant="ghost" className="text-red-600" onClick={() => run(() => deactivateUser.mutateAsync(user.id))}>
                          Deactivate
                        </Button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      {showInvite && <InviteUserDialog onClose={() => setShowInvite(false)} />}
      {assignUser && <AssignRoleDialog user={assignUser} onClose={() => setAssignUser(null)} />}
    </div>
  )
}
