<script setup lang="ts">
import { Search, X } from 'lucide-vue-next'
import { ref, watch } from 'vue'

const props = withDefaults(defineProps<{ placeholder?: string; debounce?: number; label?: string }>(), { debounce: 300 })
const model = defineModel<string>({ default: '' })
const local = ref(model.value)
let timer: ReturnType<typeof setTimeout> | undefined

watch(local, (v) => {
  clearTimeout(timer)
  timer = setTimeout(() => (model.value = v.trim()), props.debounce)
})
watch(model, (v) => {
  if (v !== local.value.trim()) local.value = v
})
</script>

<template>
  <div class="relative w-full sm:w-72">
    <input
      v-model="local"
      type="search"
      :placeholder="placeholder ?? 'Pesquisar'"
      :aria-label="label ?? placeholder ?? 'Pesquisar'"
      class="h-12 w-full rounded-2xl border border-line bg-surface pr-11 pl-4 text-[15px] text-ink transition-colors hover:border-line-strong focus:border-sel-ring focus:ring-4 focus:ring-brand-soft focus:outline-none [&::-webkit-search-cancel-button]:hidden"
      @keydown.esc="local = ''"
    />
    <button v-if="local" type="button" class="absolute top-1/2 right-3 -translate-y-1/2 rounded-full p-1 text-muted hover:text-ink" aria-label="Limpar busca" @click="local = ''">
      <X class="size-4" />
    </button>
    <Search v-else class="pointer-events-none absolute top-1/2 right-4 size-5 -translate-y-1/2 text-muted" aria-hidden="true" />
  </div>
</template>
