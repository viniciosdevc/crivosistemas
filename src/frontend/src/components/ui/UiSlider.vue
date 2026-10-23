<script setup lang="ts">
import { SliderRange, SliderRoot, SliderThumb, SliderTrack } from 'reka-ui'
import { computed } from 'vue'

/** Single-value slider (volume and similar). Keyboard arrows/Home/End and touch come from Reka. */
const props = withDefaults(defineProps<{ min?: number; max?: number; step?: number; disabled?: boolean; label?: string }>(), { min: 0, max: 100, step: 1 })
const model = defineModel<number>({ required: true })
const values = computed({
  get: () => [model.value],
  set: (v) => {
    if (v[0] !== undefined) model.value = v[0]
  },
})
</script>

<template>
  <SliderRoot
    v-model="values"
    :min="props.min"
    :max="props.max"
    :step="props.step"
    :disabled="disabled"
    class="relative flex h-6 w-full touch-none items-center select-none data-[disabled]:opacity-40"
  >
    <SliderTrack class="relative h-2 grow overflow-hidden rounded-full bg-line-strong">
      <SliderRange class="absolute h-full rounded-full bg-sel-ring" />
    </SliderTrack>
    <SliderThumb
      :aria-label="label"
      class="block size-5 rounded-full border-2 border-sel-ring bg-white shadow-sm outline-none transition-shadow hover:ring-4 hover:ring-brand-soft focus-visible:ring-4 focus-visible:ring-brand-soft data-[disabled]:cursor-not-allowed"
    />
  </SliderRoot>
</template>
