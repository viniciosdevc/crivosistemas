<script setup lang="ts">
import { Clock } from 'lucide-vue-next'
import { PopoverAnchor, PopoverContent, PopoverPortal, PopoverRoot, PopoverTrigger } from 'reka-ui'
import { computed, nextTick, ref, useId, watch } from 'vue'
import UiField from './UiField.vue'
import { inTimeRange, makeTime, maskTime, minuteOptions, normalizeTime, parseTime } from '@/lib/time'

defineOptions({ inheritAttrs: false })
const props = withDefaults(
  defineProps<{
    label?: string
    error?: string | null
    hint?: string
    optional?: boolean
    placeholder?: string
    /** 'HH:MM' bounds (same day). */
    min?: string
    max?: string
    /** Minute granularity of the picker list; typing still accepts any minute. */
    step?: number
    disabled?: boolean
    /** Forces the error look without a message (e.g. a row-level error shown elsewhere). */
    invalid?: boolean
  }>(),
  { step: 5 },
)
// Same idea as UiDatePicker: 'HH:MM' or ''. 'HH:MM:SS' coming in is shown and emitted as 'HH:MM'.
const model = defineModel<string | null>()
const open = ref(false)
const draft = ref('')
const localError = ref<string | null>(null)
const listId = useId()
const hoursEl = ref<HTMLElement | null>(null)
const minutesEl = ref<HTMLElement | null>(null)
const inputEl = ref<HTMLInputElement | null>(null)

const current = computed(() => normalizeTime(model.value))
const hour = computed(() => (current.value ? Number(current.value.slice(0, 2)) : undefined))
const minute = computed(() => (current.value ? Number(current.value.slice(3, 5)) : undefined))
const hours = Array.from({ length: 24 }, (_, i) => i)
const minutes = computed(() => minuteOptions(props.step, minute.value))
const stepSize = computed(() => Math.min(Math.max(Math.floor(props.step) || 5, 1), 30))
const pad = (n: number) => String(n).padStart(2, '0')

// Keep the text in sync with the model (external changes, selection from the lists).
watch(current, (v) => { draft.value = v; localError.value = null }, { immediate: true })
// An 'HH:MM:SS' value is rewritten once so the parent holds the normalized form.
watch(() => model.value, (v) => { if (v && v !== current.value && current.value) model.value = current.value }, { immediate: true })

const shownError = computed(() => props.error || localError.value)

function rangeError(v: string) {
  return inTimeRange(v, props.min, props.max) ? null : props.min && v < props.min ? `Use ${props.min} ou depois.` : `Use ${props.max} ou antes.`
}

function commit(v: string) {
  const err = rangeError(v)
  localError.value = err
  draft.value = v
  if (!err) model.value = v
}

/** Parses the typed text; invalid text keeps the draft and shows an error. */
function settle() {
  const text = draft.value.trim()
  if (!text) {
    localError.value = null
    if (current.value) model.value = ''
    return
  }
  const v = parseTime(text)
  if (!v) {
    localError.value = 'Hora inválida. Use o formato HH:MM.'
    return
  }
  commit(v)
}

function onInput(e: Event) {
  const el = e.target as HTMLInputElement
  const before = draft.value
  // Deleting must not be undone by the auto colon.
  const masked = (e as InputEvent).inputType?.startsWith('delete') && el.value.length < before.length ? el.value.replace(/[^\d:]/g, '').slice(0, 5) : maskTime(el.value)
  draft.value = masked
  el.value = masked
  localError.value = null
}

function onInputKey(e: KeyboardEvent) {
  if (e.key === 'Enter') {
    settle()
    if (!localError.value) open.value = false
    e.preventDefault()
  } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault()
    if (!open.value) open.value = true
    else nudge(e.key === 'ArrowDown' ? 1 : -1)
  }
}

/** Moves the value by one step from the input; wraps inside the day. */
function nudge(dir: 1 | -1) {
  const base = parseTime(draft.value) ?? current.value ?? '00:00'
  const total = Number(base.slice(0, 2)) * 60 + Number(base.slice(3, 5))
  const s = stepSize.value
  const next = (((total + dir * s) % 1440) + 1440) % 1440
  const v = makeTime(Math.floor(next / 60), next % 60)
  if (v) commit(v)
}

function pick(h: number, m: number) {
  const v = makeTime(h, m)
  if (v) commit(v)
}
function pickHour(h: number) {
  const m = minute.value ?? 0
  pick(h, m)
}
function pickMinute(m: number) {
  pick(hour.value ?? 0, m)
}

function hourDisabled(h: number) {
  return !minutes.value.some((m) => inTimeRange(`${pad(h)}:${pad(m)}`, props.min, props.max))
}
function minuteDisabled(m: number) {
  return !inTimeRange(`${pad(hour.value ?? 0)}:${pad(m)}`, props.min, props.max)
}

const nowValue = computed(() => {
  const d = new Date()
  const s = stepSize.value
  const total = Math.min(Math.round((d.getHours() * 60 + d.getMinutes()) / s) * s, 1439)
  return makeTime(Math.floor(total / 60), total % 60) ?? '00:00'
})
const nowOutOfRange = computed(() => !inTimeRange(nowValue.value, props.min, props.max))

function pickNow() {
  commit(nowValue.value)
  open.value = false
}
function clear() {
  draft.value = ''
  localError.value = null
  model.value = ''
  open.value = false
}

function scrollToSelected(focus: boolean) {
  for (const [col, sel] of [[hoursEl.value, hour.value], [minutesEl.value, minute.value]] as const) {
    if (!col) continue
    const item = col.querySelector<HTMLElement>('[aria-selected="true"]') ?? (sel === undefined ? col.querySelector<HTMLElement>('button:not(:disabled)') : null)
    if (item) col.scrollTop = item.offsetTop - col.clientHeight / 2 + item.offsetHeight / 2
  }
  if (focus) {
    const target = hoursEl.value?.querySelector<HTMLElement>('[aria-selected="true"]') ?? hoursEl.value?.querySelector<HTMLElement>('button:not(:disabled)')
    target?.focus({ preventScroll: true })
  }
}

function onOpenAutoFocus(e: Event) {
  e.preventDefault()
  void nextTick(() => scrollToSelected(true))
}
function onCloseAutoFocus(e: Event) {
  e.preventDefault()
  inputEl.value?.focus({ preventScroll: true })
}
function onInteractOutside(e: Event) {
  // Clicking the field itself should not dismiss and re-open.
  if ((e.target as HTMLElement | null)?.closest('[data-time-anchor]')) e.preventDefault()
}

/** Arrow keys move through a column (selecting as they go); left/right switch columns; Enter closes. */
function onColumnKey(e: KeyboardEvent, col: 'h' | 'm') {
  const list = (col === 'h' ? hoursEl.value : minutesEl.value)
  if (!list) return
  const items = [...list.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')]
  const idx = items.indexOf(document.activeElement as HTMLButtonElement)
  let next: HTMLButtonElement | undefined
  if (e.key === 'ArrowDown') next = items[Math.min(idx + 1, items.length - 1)]
  else if (e.key === 'ArrowUp') next = items[Math.max(idx - 1, 0)]
  else if (e.key === 'Home') next = items[0]
  else if (e.key === 'End') next = items[items.length - 1]
  else if ((e.key === 'ArrowRight' && col === 'h') || (e.key === 'ArrowLeft' && col === 'm')) {
    const other = col === 'h' ? minutesEl.value : hoursEl.value
    ;(other?.querySelector<HTMLElement>('[aria-selected="true"]') ?? other?.querySelector<HTMLElement>('button:not(:disabled)'))?.focus({ preventScroll: true })
    e.preventDefault()
    return
  } else if (e.key === 'Enter') {
    e.preventDefault()
    open.value = false
    return
  } else return
  e.preventDefault()
  if (!next) return
  next.focus({ preventScroll: true })
  next.click()
  next.scrollIntoView({ block: 'nearest' })
}

const itemClass =
  'flex h-9 w-full items-center justify-center rounded-lg text-[14px] tabular-nums text-text outline-none transition-colors hover:bg-surface-3 focus-visible:ring-4 focus-visible:ring-brand-soft aria-selected:bg-brand aria-selected:font-semibold aria-selected:text-brand-ink disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent'
</script>

<template>
  <UiField v-slot="{ id, invalid: fieldInvalid, describedby }" :label="label" :error="shownError" :hint="hint" :optional="optional">
    <PopoverRoot v-model:open="open">
      <PopoverAnchor as-child>
        <div
          data-time-anchor
          :class="[
            'group flex h-12 w-full items-center gap-2 rounded-[var(--radius-field)] border bg-surface pl-4 pr-1.5 text-[15px] text-ink transition-colors',
            open ? 'border-sel-ring ring-4 ring-brand-soft' : fieldInvalid || invalid || shownError ? 'border-danger focus-within:ring-4 focus-within:ring-danger-soft' : 'border-line hover:border-line-strong focus-within:border-sel-ring focus-within:ring-4 focus-within:ring-brand-soft',
            disabled ? 'cursor-not-allowed bg-surface-2 text-muted' : '',
          ]"
        >
          <input
            :id="id"
            ref="inputEl"
            v-bind="$attrs"
            :value="draft"
            type="text"
            inputmode="numeric"
            autocomplete="off"
            maxlength="5"
            role="combobox"
            aria-haspopup="dialog"
            :aria-expanded="open"
            :aria-controls="listId"
            :aria-invalid="fieldInvalid || invalid || !!shownError || undefined"
            :aria-describedby="describedby"
            :placeholder="placeholder || '--:--'"
            :disabled="disabled"
            class="h-full min-w-0 flex-1 bg-transparent tabular-nums outline-none placeholder:text-faint disabled:cursor-not-allowed"
            @input="onInput"
            @blur="settle"
            @keydown="onInputKey"
          />
          <PopoverTrigger
            :disabled="disabled"
            class="flex size-9 shrink-0 items-center justify-center rounded-full text-muted outline-none transition-colors hover:bg-surface-3 hover:text-ink focus-visible:ring-4 focus-visible:ring-brand-soft disabled:pointer-events-none disabled:opacity-60"
            aria-label="Escolher horário"
          >
            <Clock class="size-4" aria-hidden="true" />
          </PopoverTrigger>
        </div>
      </PopoverAnchor>

      <PopoverPortal>
        <PopoverContent
          :id="listId"
          :side-offset="6"
          :collision-padding="12"
          align="start"
          class="ui-select z-[60] w-[min(15rem,calc(100vw-1.5rem))] rounded-xl border border-line bg-surface p-3 shadow-sm"
          aria-label="Escolher horário"
          @open-auto-focus="onOpenAutoFocus"
          @close-auto-focus="onCloseAutoFocus"
          @interact-outside="onInteractOutside"
        >
          <div class="grid grid-cols-2 gap-2">
            <p class="text-center text-[12px] text-muted">Hora</p>
            <p class="text-center text-[12px] text-muted">Minuto</p>
            <div ref="hoursEl" role="listbox" aria-label="Hora" class="relative flex h-52 flex-col gap-0.5 no-scrollbar scroll-fade overflow-y-auto overscroll-contain" @keydown="onColumnKey($event, 'h')">
              <button v-for="h in hours" :key="h" type="button" role="option" :aria-selected="h === hour" :tabindex="h === (hour ?? hours.find((x) => !hourDisabled(x))) ? 0 : -1" :disabled="hourDisabled(h)" :class="itemClass" @click="pickHour(h)">{{ pad(h) }}</button>
            </div>
            <div ref="minutesEl" role="listbox" aria-label="Minuto" class="relative flex h-52 flex-col gap-0.5 no-scrollbar scroll-fade overflow-y-auto overscroll-contain" @keydown="onColumnKey($event, 'm')">
              <button v-for="m in minutes" :key="m" type="button" role="option" :aria-selected="m === minute" :tabindex="m === (minute ?? 0) ? 0 : -1" :disabled="minuteDisabled(m)" :class="itemClass" @click="pickMinute(m)">{{ pad(m) }}</button>
            </div>
          </div>

          <div class="mt-3 flex items-center justify-between gap-2 border-t border-line pt-3">
            <button
              v-if="optional"
              type="button"
              :disabled="!current && !draft"
              class="h-9 rounded-full px-3 text-sm font-medium text-muted outline-none transition-colors hover:bg-surface-3 hover:text-ink focus-visible:ring-4 focus-visible:ring-brand-soft disabled:opacity-40 disabled:hover:bg-transparent"
              @click="clear"
            >
              Limpar
            </button>
            <button
              type="button"
              :disabled="nowOutOfRange"
              class="h-9 rounded-full px-3 text-sm font-medium text-muted outline-none transition-colors hover:bg-surface-3 hover:text-ink focus-visible:ring-4 focus-visible:ring-brand-soft disabled:opacity-40 disabled:hover:bg-transparent"
              @click="pickNow"
            >
              Agora
            </button>
            <button
              type="button"
              class="ml-auto h-9 rounded-full bg-surface-3 px-4 text-sm font-medium text-ink outline-none transition-colors hover:bg-surface-2 focus-visible:ring-4 focus-visible:ring-brand-soft"
              @click="open = false"
            >
              OK
            </button>
          </div>
        </PopoverContent>
      </PopoverPortal>
    </PopoverRoot>
  </UiField>
</template>
