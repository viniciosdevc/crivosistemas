<script setup lang="ts">
/** Valor que o usuário pode trocar para simular outro preço: duplo clique (ou o lápis, no celular). */
import { Pencil } from 'lucide-vue-next'
import { nextTick, ref } from 'vue'

defineProps<{ label: string; display: string; placeholder: string; overridden: boolean; inputmode?: 'decimal' | 'numeric' }>()
const model = defineModel<string>({ required: true })

const editing = ref(false)
const input = ref<HTMLInputElement | null>(null)

async function start() {
  editing.value = true
  await nextTick()
  input.value?.select()
}
</script>

<template>
  <div class="flex min-w-0 items-center justify-between gap-2">
    <span class="min-w-0 truncate text-muted">{{ label }}</span>
    <input
      v-if="editing"
      ref="input"
      v-model="model"
      type="text"
      :inputmode="inputmode ?? 'decimal'"
      :placeholder="placeholder"
      :aria-label="`Simular ${label}`"
      class="h-10 w-32 shrink-0 rounded-xl border border-line-strong bg-surface px-3 text-right text-[15px] text-ink tabular outline-none focus:border-sel-ring"
      @blur="editing = false"
      @keydown.enter.prevent="editing = false"
      @keydown.esc.prevent="editing = false"
    />
    <span v-else class="flex shrink-0 items-center gap-1">
      <button
        type="button"
        title="Duplo clique para simular outro preço"
        :class="['rounded-lg px-1.5 py-0.5 font-semibold whitespace-nowrap tabular hover:bg-surface-3', overridden ? 'text-warning' : 'text-ink']"
        @dblclick="start"
      >{{ display }}</button>
      <button type="button" class="rounded-full p-1.5 text-faint hover:bg-surface-3 hover:text-ink" :aria-label="`Simular outro valor para ${label}`" @click="start">
        <Pencil class="size-3.5" />
      </button>
    </span>
  </div>
</template>
