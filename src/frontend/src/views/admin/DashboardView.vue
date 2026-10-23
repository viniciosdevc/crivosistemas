<script setup lang="ts">
import { ChartColumn } from 'lucide-vue-next'
import PageCard from '@/components/ui/PageCard.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
</script>

<template>
  <PageCard title="Painel" subtitle="Visão geral do negócio.">
    <UiEmptyState :icon="ChartColumn" title="Em construção" description="Os indicadores de vendas, clientes e lucro aparecerão aqui nas próximas etapas." />
  </PageCard>
</template>
