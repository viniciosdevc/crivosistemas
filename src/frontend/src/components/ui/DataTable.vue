<script setup lang="ts" generic="T">
/**
 * Table in the reference style: rounded bordered container, tinted header, generous rows.
 * Cells render through slots named `cell-<key>`; rows become links when `rowTo` is set.
 * While loading with no data it shows skeleton rows shaped like the columns; while reloading it keeps rows and dims them.
 */
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-vue-next'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import UiSkeleton from './UiSkeleton.vue'

export interface Column {
  key: string
  label: string
  sortable?: boolean
  align?: 'left' | 'right' | 'center'
  /** Tailwind width/min-width classes, e.g. "w-40" */
  class?: string
  /** Hide below this breakpoint */
  hideBelow?: 'sm' | 'md' | 'lg' | 'xl'
}

const props = withDefaults(
  defineProps<{
    columns: Column[]
    rows: T[] | null | undefined
    rowKey: (row: T) => string
    loading?: boolean
    sort?: string
    dir?: 'asc' | 'desc'
    rowTo?: (row: T) => RouteLocationRaw
    skeletonRows?: number
    caption?: string
  }>(),
  { skeletonRows: 6 },
)
const emit = defineEmits<{ sort: [key: string, dir: 'asc' | 'desc'] }>()
const router = useRouter()

const hide = { sm: 'hidden sm:table-cell', md: 'hidden md:table-cell', lg: 'hidden lg:table-cell', xl: 'hidden xl:table-cell' }
const align = { left: 'text-left', right: 'text-right', center: 'text-center' }

function toggle(col: Column) {
  if (!col.sortable) return
  emit('sort', col.key, props.sort === col.key && props.dir === 'desc' ? 'asc' : 'desc')
}

function open(row: T, e: MouseEvent) {
  if (!props.rowTo) return
  const target = e.target as HTMLElement
  if (target.closest('a,button,[role="menu"],input,label')) return
  const to = props.rowTo(row)
  if (e.metaKey || e.ctrlKey) window.open(router.resolve(to).href, '_blank')
  else router.push(to)
}
</script>

<template>
  <div class="overflow-hidden rounded-2xl border border-line bg-surface">
    <div class="scrollbar-thin overflow-x-auto">
      <table class="w-full border-collapse text-left">
        <caption v-if="caption" class="sr-only">{{ caption }}</caption>
        <thead class="bg-surface-2">
          <tr>
            <th
              v-for="col in columns"
              :key="col.key"
              scope="col"
              :aria-sort="sort === col.key ? (dir === 'asc' ? 'ascending' : 'descending') : undefined"
              :class="['h-[52px] border-b border-line px-4 text-sm font-medium whitespace-nowrap text-ink first:pl-5 last:pr-5', align[col.align ?? 'left'], col.class, col.hideBelow && hide[col.hideBelow]]"
            >
              <button
                v-if="col.sortable"
                type="button"
                class="-mx-1 inline-flex items-center gap-1.5 rounded px-1 hover:text-ink"
                @click="toggle(col)"
              >
                {{ col.label }}
                <ArrowDown v-if="sort === col.key && dir === 'desc'" class="size-3.5" />
                <ArrowUp v-else-if="sort === col.key" class="size-3.5" />
                <ArrowUpDown v-else class="size-3.5 text-faint" />
              </button>
              <template v-else>{{ col.label }}</template>
            </th>
          </tr>
        </thead>
        <tbody v-if="!rows && loading">
          <tr v-for="i in skeletonRows" :key="i" class="border-b border-line last:border-0">
            <td v-for="(col, ci) in columns" :key="col.key" :class="['h-[76px] px-4 first:pl-5 last:pr-5', col.hideBelow && hide[col.hideBelow]]">
              <UiSkeleton :class="['h-3.5', ci === 0 ? 'w-40' : 'w-20', col.align === 'right' && 'ml-auto']" />
              <UiSkeleton v-if="ci === 0" class="mt-2 h-3 w-28" />
            </td>
          </tr>
        </tbody>
        <tbody v-else-if="rows && rows.length" :class="['transition-opacity', loading && 'opacity-60']">
          <tr
            v-for="row in rows"
            :key="rowKey(row)"
            :class="['border-b border-line transition-colors last:border-0', rowTo && 'cursor-pointer hover:bg-surface-2']"
            @click="open(row, $event)"
          >
            <td
              v-for="col in columns"
              :key="col.key"
              :class="['h-[76px] px-4 py-3 text-[15px] text-text first:pl-5 last:pr-5', align[col.align ?? 'left'], col.class, col.hideBelow && hide[col.hideBelow]]"
            >
              <slot :name="`cell-${col.key}`" :row="row">{{ (row as Record<string, unknown>)[col.key] }}</slot>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="rows && !rows.length && !loading"><slot name="empty" /></div>
    <slot name="footer" />
  </div>
</template>
