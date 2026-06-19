import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Button, FieldError, Input, Label, Modal } from '@/shared/ui'
import { useCreateRole, useUpdateRole } from './api'
import type { RoleListItem } from '@/shared/api/types'
import { extractApiError } from '@/shared/api/error'
import { useState } from 'react'

const schema = z.object({
  name: z.string().min(1, 'Name is required').max(100),
  description: z.string().max(500).optional(),
})

type FormValues = z.infer<typeof schema>

export function RoleFormDialog({ role, onClose }: { role: RoleListItem | null; onClose: () => void }) {
  const isEdit = !!role
  const createRole = useCreateRole()
  const updateRole = useUpdateRole()
  const [serverError, setServerError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: role?.name ?? '', description: role?.description ?? '' },
  })

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null)
    try {
      if (isEdit && role) {
        await updateRole.mutateAsync({ id: role.id, name: values.name, description: values.description || null })
      } else {
        await createRole.mutateAsync({ name: values.name, description: values.description || null, permissions: [] })
      }
      onClose()
    } catch (error) {
      setServerError(extractApiError(error))
    }
  })

  return (
    <Modal
      title={isEdit ? 'Edit role' : 'New role'}
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={onSubmit} disabled={isSubmitting}>
            {isSubmitting ? 'Saving…' : 'Save'}
          </Button>
        </>
      }
    >
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div>
          <Label htmlFor="name">Name</Label>
          <Input id="name" {...register('name')} />
          <FieldError>{errors.name?.message}</FieldError>
        </div>
        <div>
          <Label htmlFor="description">Description</Label>
          <Input id="description" {...register('description')} />
          <FieldError>{errors.description?.message}</FieldError>
        </div>
        {serverError && <p className="text-sm text-red-600">{serverError}</p>}
      </form>
    </Modal>
  )
}
