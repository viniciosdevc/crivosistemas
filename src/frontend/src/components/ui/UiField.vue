<script setup lang="ts">
/** Label + control + hint/error. The control goes in the default slot and receives `id` and `invalid`. */
import { useId } from 'vue'

defineProps<{ label?: string; error?: string | null; hint?: string; optional?: boolean }>()
const id = useId()
</script>

<template>
  <div class="flex flex-col gap-2">
    <label v-if="label" :for="id" class="text-sm font-medium text-text">
      {{ label }}<span v-if="optional" class="font-normal text-faint"> (opcional)</span>
    </label>
    <slot :id="id" :invalid="!!error" :describedby="error || hint ? `${id}-msg` : undefined" />
    <p v-if="error" :id="`${id}-msg`" class="text-[13px] text-danger" role="alert">{{ error }}</p>
    <p v-else-if="hint" :id="`${id}-msg`" class="text-[13px] text-muted">{{ hint }}</p>
  </div>
</template>
