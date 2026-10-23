<script setup lang="ts">
/** Badge de um valor de enum da API, com rótulo e cor vindos de um mapa de `lib/labels`. */
import { computed } from 'vue'
import { labelOf, type Tone } from '@/lib/labels'
import UiBadge from './UiBadge.vue'

const props = defineProps<{ map: Record<string, { label: string; tone: Tone }>; value: string | null | undefined }>()
const info = computed(() => labelOf(props.map, props.value))
</script>

<template>
  <UiBadge :tone="info.tone">{{ info.label }}</UiBadge>
</template>
