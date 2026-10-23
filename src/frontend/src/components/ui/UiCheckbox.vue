<script setup lang="ts">
import { Check } from 'lucide-vue-next'
import { CheckboxIndicator, CheckboxRoot } from 'reka-ui'
import { useId } from 'vue'

defineProps<{ label?: string; disabled?: boolean }>()
const model = defineModel<boolean>({ default: false })
const id = useId()
</script>

<template>
  <div class="flex items-center gap-3">
    <CheckboxRoot
      :id="id"
      v-model="model"
      :disabled="disabled"
      class="flex size-5 shrink-0 items-center justify-center rounded-md border border-line-strong bg-surface transition-colors data-[state=checked]:border-sel-ring data-[state=checked]:bg-brand"
    >
      <CheckboxIndicator><Check class="size-3.5 text-brand-ink" stroke-width="3" /></CheckboxIndicator>
    </CheckboxRoot>
    <label v-if="label" :for="id" class="cursor-pointer text-[15px] text-text"><slot>{{ label }}</slot></label>
  </div>
</template>
