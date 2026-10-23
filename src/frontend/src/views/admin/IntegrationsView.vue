<script setup lang="ts">
import { Plug } from 'lucide-vue-next'
import PageCard from '@/components/ui/PageCard.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
</script>

<template>
  <PageCard title="Integrações" subtitle="Conexão com o crivonft.">
    <UiEmptyState :icon="Plug" title="Em construção" description="O status e a sincronização com o crivonft serão exibidos aqui em uma próxima etapa." />
  </PageCard>
</template>
