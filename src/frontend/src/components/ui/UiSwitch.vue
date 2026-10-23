<script setup lang="ts">
import { SwitchRoot, SwitchThumb } from 'reka-ui'
import { useId } from 'vue'

defineProps<{ label?: string; description?: string; disabled?: boolean }>()
const model = defineModel<boolean>({ default: false })
const id = useId()
</script>

<template>
  <div class="flex items-start justify-between gap-4">
    <label v-if="label" :for="id" class="flex cursor-pointer flex-col gap-0.5">
      <span class="text-[15px] font-medium text-ink">{{ label }}</span>
      <span v-if="description" class="text-sm text-muted">{{ description }}</span>
    </label>
    <SwitchRoot
      :id="id"
      v-model="model"
      :disabled="disabled"
      class="relative mt-0.5 inline-flex h-6 w-11 shrink-0 items-center rounded-full bg-line-strong transition-colors data-[state=checked]:bg-sel-ring disabled:opacity-50"
    >
      <SwitchThumb class="block size-5 translate-x-0.5 rounded-full bg-white shadow-sm transition-transform data-[state=checked]:translate-x-[22px]" />
    </SwitchRoot>
  </div>
</template>
