import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import { setUnauthorizedHandler } from './lib/api'
import { startUpdateWatcher } from './lib/updateCheck'
import { router } from './router'
import { useAuthStore } from './stores/auth'
import './styles/main.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)

setUnauthorizedHandler(() => {
  useAuthStore().expire()
  if (router.currentRoute.value.path.startsWith('/painel'))
    router.replace({ name: 'login', query: { next: router.currentRoute.value.fullPath, expired: '1' } })
})

app.mount('#app')
startUpdateWatcher()
