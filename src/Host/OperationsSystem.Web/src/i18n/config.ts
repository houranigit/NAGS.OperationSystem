import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './locales/en.json'
import ar from './locales/ar.json'

export const SUPPORTED_LANGUAGES = ['en', 'ar'] as const
export type Language = (typeof SUPPORTED_LANGUAGES)[number]

export const RTL_LANGUAGES: Language[] = ['ar']

const STORAGE_KEY = 'opsys.lang'

export function getStoredLanguage(): Language {
  const stored = localStorage.getItem(STORAGE_KEY)
  return stored === 'ar' || stored === 'en' ? stored : 'en'
}

export function storeLanguage(language: Language) {
  localStorage.setItem(STORAGE_KEY, language)
}

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    ar: { translation: ar },
  },
  lng: getStoredLanguage(),
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  returnEmptyString: false,
})

export default i18n
