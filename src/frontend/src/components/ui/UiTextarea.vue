<script setup lang="ts">
import UiField from './UiField.vue'
import { fieldClasses } from './fieldClasses'

defineOptions({ inheritAttrs: false })
defineProps<{ label?: string; error?: string | null; hint?: string; optional?: boolean }>()
const model = defineModel<string | null>()
</script>

<template>
  <UiField v-slot="{ id, invalid, describedby }" :label="label" :error="error" :hint="hint" :optional="optional">
    <textarea
      :id="id"
      v-model="model"
      rows="3"
      v-bind="$attrs"
      :aria-invalid="invalid || undefined"
      :aria-describedby="describedby"
      :class="fieldClasses(invalid, 'min-h-24 resize-y py-3 leading-relaxed')"
    />
  </UiField>
</template>
