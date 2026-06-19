import { useTranslation } from 'react-i18next'
import { ChevronLeftIcon, ChevronRightIcon } from 'lucide-react'
import { Button } from '@/components/ui/button'
import type { PagedResult } from '@/shared/api/types'

export function Pagination<T>({
  result,
  onPageChange,
}: {
  result: Pick<PagedResult<T>, 'page' | 'totalPages' | 'hasPreviousPage' | 'hasNextPage' | 'totalCount'>
  onPageChange: (page: number) => void
}) {
  const { t } = useTranslation()

  if (result.totalPages <= 1) {
    return (
      <div className="flex items-center px-1 text-sm text-muted-foreground">
        {t('common.resultsCount', { count: result.totalCount })}
      </div>
    )
  }

  return (
    <div className="flex items-center justify-between gap-2 px-1">
      <span className="text-sm text-muted-foreground">
        {t('common.page', { page: result.page, totalPages: result.totalPages })}
      </span>
      <div className="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          disabled={!result.hasPreviousPage}
          onClick={() => onPageChange(result.page - 1)}
        >
          <ChevronLeftIcon data-icon="inline-start" className="rtl:rotate-180" />
          {t('common.previous')}
        </Button>
        <Button
          variant="outline"
          size="sm"
          disabled={!result.hasNextPage}
          onClick={() => onPageChange(result.page + 1)}
        >
          {t('common.next')}
          <ChevronRightIcon data-icon="inline-end" className="rtl:rotate-180" />
        </Button>
      </div>
    </div>
  )
}
