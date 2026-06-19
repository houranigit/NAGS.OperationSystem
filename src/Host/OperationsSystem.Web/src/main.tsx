import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import './i18n/config'
import App from './App.tsx'
import { queryClient } from '@/app/queryClient'
import { AuthProvider } from '@/shared/auth/auth-context'
import { LanguageProvider } from '@/i18n/LanguageProvider'
import { Toaster } from '@/components/ui/sonner'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LanguageProvider>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <AuthProvider>
            <App />
          </AuthProvider>
        </BrowserRouter>
        <Toaster position="top-center" richColors />
      </QueryClientProvider>
    </LanguageProvider>
  </StrictMode>,
)
