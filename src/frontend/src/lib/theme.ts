import { ref } from 'vue'

export const isDark = ref(document.documentElement.classList.contains('dark'))

function applyTheme(dark: boolean) {
  isDark.value = dark
  document.documentElement.classList.toggle('dark', dark)
  try {
    localStorage.setItem('crv-theme', dark ? 'dark' : 'light')
  } catch {
    /* ignore */
  }
}

/** Switches theme with a circle that grows from the clicked button (View Transitions); plain switch when unsupported or motion is reduced. */
export function toggleTheme(event?: Event) {
  const next = !isDark.value
  const doc = document as Document & { startViewTransition?: (cb: () => void) => { ready: Promise<void> } }
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (!doc.startViewTransition || reduced) return applyTheme(next)

  const target = (event?.currentTarget ?? event?.target) as HTMLElement | null
  const box = target?.getBoundingClientRect?.()
  const x = box ? box.left + box.width / 2 : window.innerWidth - 40
  const y = box ? box.top + box.height / 2 : 40
  const radius = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y))
  const transition = doc.startViewTransition(() => applyTheme(next))
  transition.ready
    .then(() =>
      document.documentElement.animate(
        { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
        { duration: 550, easing: 'cubic-bezier(0.22, 1, 0.36, 1)', pseudoElement: '::view-transition-new(root)' },
      ),
    )
    .catch(() => {})
}

export function restoreTheme() {
  document.documentElement.classList.toggle('dark', isDark.value)
}
