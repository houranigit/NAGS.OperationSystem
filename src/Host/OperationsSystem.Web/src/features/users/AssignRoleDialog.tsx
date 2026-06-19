import { useState } from 'react'
import { Button, Label, Modal, Select } from '@/shared/ui'
import { useAssignRole } from './api'
import { useRoles } from '@/features/roles/api'
import type { UserListItem } from '@/shared/api/types'
import { extractApiError } from '@/shared/api/error'

export function AssignRoleDialog({ user, onClose }: { user: UserListItem; onClose: () => void }) {
  const roles = useRoles('')
  const assignRole = useAssignRole()
  const [roleId, setRoleId] = useState(user.roleId)
  const [serverError, setServerError] = useState<string | null>(null)

  const save = async () => {
    setServerError(null)
    try {
      await assignRole.mutateAsync({ id: user.id, roleId })
      onClose()
    } catch (error) {
      setServerError(extractApiError(error))
    }
  }

  return (
    <Modal
      title={`Assign role — ${user.displayName}`}
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={save} disabled={assignRole.isPending}>
            {assignRole.isPending ? 'Saving…' : 'Save'}
          </Button>
        </>
      }
    >
      <Label htmlFor="role">Role</Label>
      <Select id="role" value={roleId} onChange={(e) => setRoleId(e.target.value)}>
        {roles.data?.items.map((role) => (
          <option key={role.id} value={role.id}>
            {role.name}
          </option>
        ))}
      </Select>
      {serverError && <p className="mt-2 text-sm text-red-600">{serverError}</p>}
    </Modal>
  )
}
