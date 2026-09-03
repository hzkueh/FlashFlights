import { MoonIcon, SunIcon } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { useTheme } from '@/hooks/use-theme'

export function ThemeToggle() {
  const { theme, toggleTheme } = useTheme()
  const next = theme === 'dark' ? 'light' : 'dark'

  return (
    <Button variant="ghost" size="icon" onClick={toggleTheme} aria-label={`Switch to ${next} theme`}>
      {theme === 'dark' ? <MoonIcon /> : <SunIcon />}
    </Button>
  )
}
