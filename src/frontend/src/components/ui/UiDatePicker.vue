<script setup lang="ts">
import { getLocalTimeZone, parseDate, today, type DateValue } from '@internationalized/date'
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-vue-next'
import {
  DatePickerCalendar,
  DatePickerCell,
  DatePickerCellTrigger,
  DatePickerContent,
  DatePickerGrid,
  DatePickerGridBody,
  DatePickerGridHead,
  DatePickerGridRow,
  DatePickerHeadCell,
  DatePickerHeader,
  DatePickerHeading,
  DatePickerNext,
  DatePickerPrev,
  DatePickerRoot,
  DatePickerTrigger,
} from 'reka-ui'
import { computed, ref } from 'vue'
import UiField from './UiField.vue'

defineOptions({ inheritAttrs: false })
const props = defineProps<{
  label?: string
  error?: string | null
  hint?: string
  optional?: boolean
  placeholder?: string
  min?: string
  max?: string
  disabled?: boolean
}>()
// Same contract as the native date input: 'YYYY-MM-DD' or ''.
const model = defineModel<string | null>()
const open = ref(false)

const ISO = /^\d{4}-\d{2}-\d{2}$/

function toDateValue(iso: string | null | undefined): DateValue | undefined {
  if (!iso || !ISO.test(iso)) return undefined
  try {
    return parseDate(iso)
  } catch {
    return undefined
  }
}

const dateValue = computed<DateValue | undefined>({
  get: () => toDateValue(model.value),
  set: (v) => {
    model.value = v ? v.toString() : ''
  },
})
const minValue = computed(() => toDateValue(props.min))
const maxValue = computed(() => toDateValue(props.max))
// Trigger text is dd/mm/aaaa (pt-BR), derived from the ISO string.
const display = computed(() => (model.value && ISO.test(model.value) ? model.value.split('-').reverse().join('/') : ''))

const todayValue = today(getLocalTimeZone())
const todayOutOfRange = computed(
  () => (!!minValue.value && todayValue.compare(minValue.value) < 0) || (!!maxValue.value && todayValue.compare(maxValue.value) > 0),
)

function pickToday() {
  dateValue.value = todayValue
  open.value = false
}

function clear() {
  dateValue.value = undefined
  open.value = false
}
</script>

<template>
  <UiField v-slot="{ id, invalid, describedby }" :label="label" :error="error" :hint="hint" :optional="optional">
    <DatePickerRoot
      v-model="dateValue"
      v-model:open="open"
      locale="pt-BR"
      :week-starts-on="0"
      weekday-format="short"
      :min-value="minValue"
      :max-value="maxValue"
      :disabled="disabled"
      fixed-weeks
    >
      <DatePickerTrigger
        :id="id"
        v-bind="$attrs"
        :aria-invalid="invalid || undefined"
        :aria-describedby="describedby"
        :class="[
          'group flex h-12 w-full items-center gap-2 rounded-[var(--radius-field)] border bg-surface px-4 text-left text-[15px] text-ink transition-colors outline-none',
          'data-[state=open]:border-sel-ring data-[state=open]:ring-4 data-[state=open]:ring-brand-soft',
          invalid ? 'border-danger focus-visible:ring-4 focus-visible:ring-danger-soft' : 'border-line hover:border-line-strong focus-visible:border-sel-ring focus-visible:ring-4 focus-visible:ring-brand-soft',
          'disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-muted',
        ]"
      >
        <span :class="['min-w-0 flex-1 truncate tabular-nums', display ? '' : 'text-faint']">{{ display || placeholder || 'Selecionar data' }}</span>
        <CalendarDays class="size-4 shrink-0 text-muted transition-colors group-data-[state=open]:text-ink" aria-hidden="true" />
      </DatePickerTrigger>

      <DatePickerContent
        :side-offset="6"
        :collision-padding="12"
        class="ui-select z-[60] w-[min(21rem,calc(100vw-1.5rem))] rounded-xl border border-line bg-surface p-4 shadow-sm"
      >
        <DatePickerCalendar v-slot="{ weekDays, grid }">
          <DatePickerHeader class="flex items-center justify-between">
            <DatePickerPrev class="rounded-full p-2 text-muted hover:bg-surface-3 hover:text-ink focus-visible:ring-4 focus-visible:ring-brand-soft outline-none disabled:opacity-40 disabled:hover:bg-transparent" aria-label="Mês anterior">
              <ChevronLeft class="size-4" aria-hidden="true" />
            </DatePickerPrev>
            <DatePickerHeading class="text-[15px] font-semibold text-ink first-letter:uppercase" />
            <DatePickerNext class="rounded-full p-2 text-muted hover:bg-surface-3 hover:text-ink focus-visible:ring-4 focus-visible:ring-brand-soft outline-none disabled:opacity-40 disabled:hover:bg-transparent" aria-label="Próximo mês">
              <ChevronRight class="size-4" aria-hidden="true" />
            </DatePickerNext>
          </DatePickerHeader>

          <DatePickerGrid v-for="month in grid" :key="month.value.toString()" class="mt-3 w-full border-collapse select-none">
            <DatePickerGridHead>
              <DatePickerGridRow>
                <DatePickerHeadCell v-for="day in weekDays" :key="day" class="pb-2 text-center text-[12px] font-normal text-muted">{{ day }}</DatePickerHeadCell>
              </DatePickerGridRow>
            </DatePickerGridHead>
            <DatePickerGridBody>
              <DatePickerGridRow v-for="(weekDates, i) in month.rows" :key="i">
                <DatePickerCell v-for="d in weekDates" :key="d.toString()" :date="d" class="p-0.5 text-center">
                  <DatePickerCellTrigger
                    :day="d"
                    :month="month.value"
                    class="mx-auto flex size-9 items-center justify-center rounded-lg text-[14px] text-text outline-none transition-colors hover:bg-surface-3 focus-visible:ring-4 focus-visible:ring-brand-soft data-[outside-view]:text-faint data-[today]:ring-1 data-[today]:ring-line-strong data-[today]:ring-inset data-[selected]:bg-brand data-[selected]:font-semibold data-[selected]:text-brand-ink data-[selected]:ring-0 data-[disabled]:cursor-not-allowed data-[disabled]:opacity-40 data-[disabled]:hover:bg-transparent data-[unavailable]:line-through"
                  />
                </DatePickerCell>
              </DatePickerGridRow>
            </DatePickerGridBody>
          </DatePickerGrid>
        </DatePickerCalendar>

        <div class="mt-3 flex items-center justify-between gap-2 border-t border-line pt-3">
          <button
            type="button"
            :disabled="!model"
            class="h-9 rounded-full px-3 text-sm font-medium text-muted outline-none transition-colors hover:bg-surface-3 hover:text-ink focus-visible:ring-4 focus-visible:ring-brand-soft disabled:opacity-40 disabled:hover:bg-transparent"
            @click="clear"
          >
            Limpar
          </button>
          <button
            type="button"
            :disabled="todayOutOfRange"
            class="h-9 rounded-full bg-surface-3 px-4 text-sm font-medium text-ink outline-none transition-colors hover:bg-surface-2 focus-visible:ring-4 focus-visible:ring-brand-soft disabled:opacity-40"
            @click="pickToday"
          >
            Hoje
          </button>
        </div>
      </DatePickerContent>
    </DatePickerRoot>
  </UiField>
</template>
