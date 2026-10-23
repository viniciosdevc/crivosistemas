<script setup lang="ts">
import UiField from './UiField.vue'
import { fieldClasses } from './fieldClasses'

defineOptions({ inheritAttrs: false })
defineProps<{ label?: string; error?: string | null; hint?: string; optional?: boolean; prefix?: string; suffix?: string }>()
const model = defineModel<string | number | null>()
</script>

<template>
  <UiField v-slot="{ id, invalid, describedby }" :label="label" :error="error" :hint="hint" :optional="optional">
    <div class="relative flex items-center">
      <span v-if="prefix || $slots.prefix" class="pointer-events-none absolute left-4 flex items-center text-[15px] text-muted"><slot name="prefix">{{ prefix }}</slot></span>
      <input
        :id="id"
        v-model="model"
        v-bind="$attrs"
        :aria-invalid="invalid || undefined"
        :aria-describedby="describedby"
        :class="fieldClasses(invalid, ['h-12', prefix || $slots.prefix ? 'pl-11' : '', suffix ? 'pr-12' : ''].join(' '))"
      />
      <span v-if="suffix" class="pointer-events-none absolute right-4 text-sm text-muted">{{ suffix }}</span>
    </div>
  </UiField>
</template>
