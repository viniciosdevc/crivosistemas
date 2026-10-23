import { ref } from 'vue'

declare const __BUILD_ID__: string

/** True when the server is serving a newer build than the one running in this tab. */
export const updateAvailable = ref(false)

const RELOAD_KEY = 'crv-chunk-reload'

async function check() {
  try {
    const res = await fetch(`/version.json?t=${Date.now()}`, { cache: 'no-store' })
    if (!res.ok || !res.headers.get('content-type')?.includes('json')) return
    const data = (await res.json()) as { build?: string }
    if (data.build && data.build !== __BUILD_ID__) updateAvailable.value = true
  } catch {
    /* offline or mid-deploy: try again later */
  }
}

/** Watches for a new deploy (every 5 min and when the tab regains focus) and recovers once from stale lazy-loaded chunks. */
export function startUpdateWatcher() {
  if (import.meta.env.DEV) return
  window.setInterval(check, 5 * 60 * 1000)
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') void check()
  })
  // After a deploy the old chunk names no longer exist: reload once to get the new build, never in a loop.
  window.addEventListener('vite:preloadError', (event) => {
    event.preventDefault()
    try {
      if (sessionStorage.getItem(RELOAD_KEY)) return
      sessionStorage.setItem(RELOAD_KEY, '1')
    } catch {
      /* ignore */
    }
    window.location.reload()
  })
  window.addEventListener('load', () => window.setTimeout(() => sessionStorage.removeItem(RELOAD_KEY), 10_000))
}
