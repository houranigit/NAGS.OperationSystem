import { toast } from 'sonner'
import { extractApiError } from '@/shared/api/error'

export function toastError(error: unknown, fallback?: string) {
  toast.error(extractApiError(error, fallback))
}

export function toastSuccess(message: string) {
  toast.success(message)
}
