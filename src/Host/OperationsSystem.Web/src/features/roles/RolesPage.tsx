import { useState } from 'react'
import { Badge, Button, Card, EmptyState, Input, Spinner } from '@/shared/ui'
import { useAuth } from '@/shared/auth/auth-context'
import { useDeleteRole, useRoles } from './api'
import { RoleFormDialog } from './RoleFormDialog'
import { PermissionsDialog } from './PermissionsDialog'
import type { RoleListItem } from '@/shared/api/types'
import { extractApiError } from '@/shared/api/error'

export function RolesPage() {
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const { data, isLoading } = useRoles(search)
  const deleteRole = useDeleteRole()

  const [formRole, setFormRole] = useState<RoleListItem | null>(null)
  const [showForm, setShowForm] = useState(false)
  const [permsRole, setPermsRole] = useState<RoleListItem | null>(null)

  const canCreate = hasPermission('identity.roles.create')
  const canUpdate = hasPermission('identity.roles.update')
  const canManagePerms = hasPermission('identity.roles.manage-permissions')
  const canDelete = hasPermission('identity.roles.delete')

  const onDelete = async (role: RoleListItem) => {
    if (!confirm(`Delete role "${role.name}"?`)) return
    try {
      await deleteRole.mutateAsync(role.id)
    } catch (error) {
      alert(extractApiError(error))
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-slate-900">Roles</h1>
        {canCreate && (
          <Button
            onClick={() => {
              setFormRole(null)
              setShowForm(true)
            }}
          >
            New role
          </Button>
        )}
      </div>

      <Input placeholder="Search roles…" value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />

      <Card>
        {isLoading ? (
          <div className="flex justify-center py-12">
            <Spinner />
          </div>
        ) : !data || data.items.length === 0 ? (
          <EmptyState message="No roles found." />
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
                <th className="px-4 py-3 font-medium">Name</th>
                <th className="px-4 py-3 font-medium">Description</th>
                <th className="px-4 py-3 font-medium">Permissions</th>
                <th className="px-4 py-3 font-medium">Users</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {data.items.map((role) => (
                <tr key={role.id} className="border-b border-slate-100 last:border-0">
                  <td className="px-4 py-3 font-medium text-slate-900">
                    {role.name} {role.isSystem && <Badge tone="amber">system</Badge>}
                  </td>
                  <td className="px-4 py-3 text-slate-600">{role.description ?? '—'}</td>
                  <td className="px-4 py-3 text-slate-600">{role.permissionCount}</td>
                  <td className="px-4 py-3 text-slate-600">{role.userCount}</td>
                  <td className="px-4 py-3">
                    <div className="flex justify-end gap-2">
                      {canManagePerms && !role.isSystem && (
                        <Button variant="ghost" onClick={() => setPermsRole(role)}>
                          Permissions
                        </Button>
                      )}
                      {canUpdate && !role.isSystem && (
                        <Button
                          variant="ghost"
                          onClick={() => {
                            setFormRole(role)
                            setShowForm(true)
                          }}
                        >
                          Edit
                        </Button>
                      )}
                      {canDelete && !role.isSystem && (
                        <Button variant="ghost" className="text-red-600" onClick={() => onDelete(role)}>
                          Delete
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

      {showForm && <RoleFormDialog role={formRole} onClose={() => setShowForm(false)} />}
      {permsRole && <PermissionsDialog role={permsRole} onClose={() => setPermsRole(null)} />}
    </div>
  )
}
