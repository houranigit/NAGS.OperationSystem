import { useEffect, useState } from 'react'
import { Button, Modal, Spinner } from '@/shared/ui'
import { usePermissionCatalog, useRole, useUpdateRolePermissions } from './api'
import type { RoleListItem } from '@/shared/api/types'
import { extractApiError } from '@/shared/api/error'

export function PermissionsDialog({ role, onClose }: { role: RoleListItem; onClose: () => void }) {
  const catalog = usePermissionCatalog()
  const roleQuery = useRole(role.id)
  const updatePermissions = useUpdateRolePermissions()
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [serverError, setServerError] = useState<string | null>(null)

  useEffect(() => {
    if (roleQuery.data) setSelected(new Set(roleQuery.data.permissions))
  }, [roleQuery.data])

  const toggle = (permission: string) => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(permission)) next.delete(permission)
      else next.add(permission)
      return next
    })
  }

  const save = async () => {
    setServerError(null)
    try {
      await updatePermissions.mutateAsync({ id: role.id, permissions: [...selected] })
      onClose()
    } catch (error) {
      setServerError(extractApiError(error))
    }
  }

  const loading = catalog.isLoading || roleQuery.isLoading

  return (
    <Modal
      title={`Permissions — ${role.name}`}
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={save} disabled={updatePermissions.isPending || loading}>
            {updatePermissions.isPending ? 'Saving…' : 'Save permissions'}
          </Button>
        </>
      }
    >
      {loading ? (
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      ) : (
        <div className="max-h-96 space-y-4 overflow-auto">
          {catalog.data?.map((group) => (
            <div key={group.resource}>
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">{group.resource}</h3>
              <div className="space-y-1">
                {group.permissions.map((permission) => (
                  <label key={permission} className="flex items-center gap-2 text-sm text-slate-700">
                    <input
                      type="checkbox"
                      checked={selected.has(permission)}
                      onChange={() => toggle(permission)}
                      className="h-4 w-4 rounded border-slate-300"
                    />
                    {permission}
                  </label>
                ))}
              </div>
            </div>
          ))}
          {serverError && <p className="text-sm text-red-600">{serverError}</p>}
        </div>
      )}
    </Modal>
  )
}
