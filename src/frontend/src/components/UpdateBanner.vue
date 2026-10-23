<script setup lang="ts">
import { RefreshCw } from 'lucide-vue-next'
import { updateAvailable } from '@/lib/updateCheck'

const reload = () => window.location.reload()
</script>

<template>
  <Transition name="update-banner">
    <div v-if="updateAvailable" class="fixed inset-x-0 bottom-4 z-[90] mx-auto flex w-fit max-w-[calc(100vw-2rem)] items-center gap-3 rounded-full bg-[#0f2a2e] py-2 pl-5 pr-2 text-sm text-white shadow-lg" role="status">
      <span>Nova versão disponível</span>
      <button type="button" class="inline-flex h-9 items-center gap-1.5 rounded-full bg-white px-4 font-medium text-[#0f2a2e] hover:bg-white/90" @click="reload">
        <RefreshCw class="size-4" aria-hidden="true" /> Atualizar
      </button>
    </div>
  </Transition>
</template>

<style scoped>
.update-banner-enter-active,
.update-banner-leave-active {
  transition: opacity 0.25s ease, transform 0.25s ease;
}
.update-banner-enter-from,
.update-banner-leave-to {
  opacity: 0;
  transform: translateY(12px);
}
</style>
