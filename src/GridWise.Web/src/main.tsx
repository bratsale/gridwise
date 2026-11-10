import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { initAuth } from './shared/auth/auth.ts'
import { LanguageProvider } from './shared/i18n/LanguageProvider.tsx'
import { ThemeProvider } from './shared/theme/ThemeProvider.tsx'

// The app renders at once ("Signing in…") while the login is finished or the session restored.
void initAuth()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LanguageProvider>
      <ThemeProvider>
        <App />
      </ThemeProvider>
    </LanguageProvider>
  </StrictMode>,
)
