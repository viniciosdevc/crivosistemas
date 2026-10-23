<script setup lang="ts">
import { ChevronLeft, ChevronRight } from 'lucide-vue-next'
import { computed } from 'vue'

const props = defineProps<{ page: number; pageSize: number; total: number }>()
const emit = defineEmits<{ 'update:page': [page: number] }>()
const pages = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))
const from = computed(() => (props.total === 0 ? 0 : (props.page - 1) * props.pageSize + 1))
const to = computed(() => Math.min(props.total, props.page * props.pageSize))
</script>

<template>
  <div v-if="total > 0" class="flex items-center justify-between gap-4 border-t border-line px-5 py-3.5 text-sm text-muted">
    <span class="tabular">{{ from }}–{{ to }} de {{ total }}</span>
    <div v-if="pages > 1" class="flex items-center gap-1">
      <button
        type="button"
        class="inline-flex size-9 items-center justify-center rounded-full hover:bg-surface-3 disabled:opacity-40 disabled:hover:bg-transparent"
        :disabled="page <= 1"
        aria-label="Página anterior"
        @click="emit('update:page', page - 1)"
      >
        <ChevronLeft class="size-4" />
      </button>
      <span class="px-2 tabular text-text">{{ page }} / {{ pages }}</span>
      <button
        type="button"
        class="inline-flex size-9 items-center justify-center rounded-full hover:bg-surface-3 disabled:opacity-40 disabled:hover:bg-transparent"
        :disabled="page >= pages"
        aria-label="Próxima página"
        @click="emit('update:page', page + 1)"
      >
        <ChevronRight class="size-4" />
      </button>
    </div>
  </div>
</template>
