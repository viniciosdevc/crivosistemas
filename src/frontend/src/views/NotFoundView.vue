<script setup lang="ts">
import { SearchX } from 'lucide-vue-next'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
</script>

<template>
  <div class="flex min-h-dvh items-center justify-center bg-canvas p-6">
    <div class="w-full max-w-md rounded-[var(--radius-card)] bg-surface">
      <UiEmptyState :icon="SearchX" title="Página não encontrada" description="O endereço pode estar errado ou a página foi movida.">
        <UiButton to="/">Ir para o início</UiButton>
      </UiEmptyState>
    </div>
  </div>
</template>
