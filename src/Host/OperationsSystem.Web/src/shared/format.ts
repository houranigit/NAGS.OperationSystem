import type { Language } from '@/i18n/config'

const localeFor: Record<Language, string> = {
  en: 'en-US',
  ar: 'ar',
}

export function formatDateTime(value: string | null | undefined, language: Language): string | null {
  if (!value) return null
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  return new Intl.DateTimeFormat(localeFor[language], {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

export function formatDate(value: string | null | undefined, language: Language): string | null {
  if (!value) return null
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  return new Intl.DateTimeFormat(localeFor[language], { dateStyle: 'medium' }).format(date)
}
