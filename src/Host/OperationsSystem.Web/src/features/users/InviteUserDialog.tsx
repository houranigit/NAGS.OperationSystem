import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Button, FieldError, Input, Label, Modal, Select } from '@/shared/ui'
import { useInviteUser } from './api'
import { useRoles } from '@/features/roles/api'
import { extractApiError } from '@/shared/api/error'

const schema = z.object({
  email: z.string().min(1, 'Email is required').email('Enter a valid email'),
  displayName: z.string().min(1, 'Display name is required').max(150),
  roleId: z.string().min(1, 'Role is required'),
})

type FormValues = z.infer<typeof schema>

export function InviteUserDialog({ onClose }: { onClose: () => void }) {
  const roles = useRoles('')
  const inviteUser = useInviteUser()
  const [serverError, setServerError] = useState<string | null>(null)
  const [invitationToken, setInvitationToken] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '', displayName: '', roleId: '' } })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      const result = await inviteUser.mutateAsync(values)
      setInvitationToken(result.invitationToken)
    } catch (error) {
      setServerError(extractApiError(error))
    }
  })

  return (
    <Modal
      title="Invite user"
      onClose={onClose}
      footer={
        invitationToken ? (
          <Button onClick={onClose}>Done</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button onClick={onSubmit} disabled={isSubmitting}>
              {isSubmitting ? 'Inviting…' : 'Send invitation'}
            </Button>
          </>
        )
      }
    >
      {invitationToken ? (
        <div className="space-y-2 text-sm">
          <p className="text-slate-700">Invitation created. Share this activation token (dev only):</p>
          <code className="block break-all rounded bg-slate-100 p-2 text-xs">{invitationToken}</code>
        </div>
      ) : (
        <form onSubmit={onSubmit} className="space-y-4" noValidate>
          <div>
            <Label htmlFor="email">Email</Label>
            <Input id="email" type="email" {...register('email')} />
            <FieldError>{errors.email?.message}</FieldError>
          </div>
          <div>
            <Label htmlFor="displayName">Display name</Label>
            <Input id="displayName" {...register('displayName')} />
            <FieldError>{errors.displayName?.message}</FieldError>
          </div>
          <div>
            <Label htmlFor="roleId">Role</Label>
            <Select id="roleId" {...register('roleId')}>
              <option value="">Select a role…</option>
              {roles.data?.items.map((role) => (
                <option key={role.id} value={role.id}>
                  {role.name}
                </option>
              ))}
            </Select>
            <FieldError>{errors.roleId?.message}</FieldError>
          </div>
          {serverError && <p className="text-sm text-red-600">{serverError}</p>}
        </form>
      )}
    </Modal>
  )
}
