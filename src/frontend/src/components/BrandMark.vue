<script setup lang="ts">
withDefaults(defineProps<{ compact?: boolean }>(), { compact: false })
</script>

<template>
  <span class="inline-flex items-center gap-2 text-ink" aria-label="Crivo Gestão">
    <img src="/logo.svg" alt="" class="size-8 shrink-0" width="32" height="32" />
    <span v-if="!compact" class="text-[21px] font-semibold tracking-tight">Crivo Gestão</span>
  </span>
</template>
