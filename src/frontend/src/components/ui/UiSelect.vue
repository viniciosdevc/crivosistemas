<script setup lang="ts" generic="T extends string | number | null">
import { Check, ChevronDown } from 'lucide-vue-next'
import { SelectContent, SelectItem, SelectItemIndicator, SelectItemText, SelectPortal, SelectRoot, SelectTrigger, SelectViewport } from 'reka-ui'
import { computed } from 'vue'
import UiField from './UiField.vue'

defineOptions({ inheritAttrs: false })
const props = defineProps<{
  label?: string
  error?: string | null
  hint?: string
  optional?: boolean
  options: { value: T; label: string; disabled?: boolean }[]
  placeholder?: string
  size?: 'md' | 'sm'
}>()
const model = defineModel<T>()

// Reka items need non-empty string values, so each option is keyed by its position.
const selectedIndex = computed(() => (model.value == null ? -1 : props.options.findIndex((o) => o.value === model.value)))
const selectedKey = computed(() => (selectedIndex.value < 0 ? undefined : `opt-${selectedIndex.value}`))
const selectedLabel = computed(() => props.options[selectedIndex.value]?.label)

function onSelect(key: unknown) {
  if (typeof key !== 'string') return
  const option = props.options[Number(key.slice(4))]
  if (option && !option.disabled) model.value = option.value
}
</script>

<template>
  <UiField v-slot="{ id, invalid, describedby }" :label="label" :error="error" :hint="hint" :optional="optional">
    <SelectRoot :model-value="selectedKey" @update:model-value="onSelect">
      <SelectTrigger
        :id="id"
        v-bind="$attrs"
        :aria-invalid="invalid || undefined"
        :aria-describedby="describedby"
        :class="[
          'group flex w-full items-center gap-2 border bg-surface px-4 text-left text-[15px] text-ink transition-colors outline-none',
          'rounded-[var(--radius-field)] data-[state=open]:border-sel-ring data-[state=open]:ring-4 data-[state=open]:ring-brand-soft',
          size === 'sm' ? 'h-11 rounded-full' : 'h-12',
          invalid ? 'border-danger focus-visible:ring-4 focus-visible:ring-danger-soft' : 'border-line hover:border-line-strong focus-visible:border-sel-ring focus-visible:ring-4 focus-visible:ring-brand-soft',
          'disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-muted',
        ]"
      >
        <span :class="['min-w-0 flex-1 truncate', selectedLabel ? '' : 'text-faint']">{{ selectedLabel ?? placeholder ?? '' }}</span>
        <ChevronDown class="size-4 shrink-0 text-muted transition-transform duration-200 group-data-[state=open]:rotate-180" aria-hidden="true" />
      </SelectTrigger>
      <SelectPortal>
        <SelectContent
          position="popper"
          :side-offset="6"
          :collision-padding="12"
          class="ui-select z-[60] min-w-[var(--reka-select-trigger-width)] overflow-hidden rounded-xl border border-line bg-surface shadow-sm"
        >
          <SelectViewport class="scrollbar-thin max-h-[min(18rem,var(--reka-select-content-available-height))] overflow-y-auto p-1.5">
            <SelectItem
              v-for="(o, i) in options"
              :key="i"
              :value="`opt-${i}`"
              :disabled="o.disabled"
              class="relative flex h-12 cursor-pointer items-center rounded-lg py-2 pr-10 pl-3 text-[15px] text-text outline-none select-none sm:h-10 data-[disabled]:cursor-not-allowed data-[disabled]:opacity-40 data-[highlighted]:bg-surface-3 data-[highlighted]:text-ink data-[state=checked]:font-medium data-[state=checked]:text-sel-text"
            >
              <SelectItemText>{{ o.label }}</SelectItemText>
              <SelectItemIndicator class="absolute right-3 flex items-center">
                <Check class="size-4 text-ink" aria-hidden="true" />
              </SelectItemIndicator>
            </SelectItem>
          </SelectViewport>
        </SelectContent>
      </SelectPortal>
    </SelectRoot>
  </UiField>
</template>
