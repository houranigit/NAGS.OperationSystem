import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import i18n, { RTL_LANGUAGES, getStoredLanguage, storeLanguage, type Language } from './config'

type Direction = 'ltr' | 'rtl'

interface LanguageContextValue {
  language: Language
  direction: Direction
  setLanguage: (language: Language) => void
}

const LanguageContext = createContext<LanguageContextValue | undefined>(undefined)

function directionFor(language: Language): Direction {
  return RTL_LANGUAGES.includes(language) ? 'rtl' : 'ltr'
}

function applyDocumentLanguage(language: Language) {
  const direction = directionFor(language)
  document.documentElement.lang = language
  document.documentElement.dir = direction
}

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(getStoredLanguage)

  useEffect(() => {
    applyDocumentLanguage(language)
  }, [language])

  const setLanguage = useCallback((next: Language) => {
    storeLanguage(next)
    void i18n.changeLanguage(next)
    setLanguageState(next)
  }, [])

  const value = useMemo<LanguageContextValue>(
    () => ({ language, direction: directionFor(language), setLanguage }),
    [language, setLanguage],
  )

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useLanguage() {
  const ctx = useContext(LanguageContext)
  if (!ctx) throw new Error('useLanguage must be used within a LanguageProvider')
  return ctx
}
