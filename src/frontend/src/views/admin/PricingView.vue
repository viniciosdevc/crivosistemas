<script setup lang="ts">
import { Calculator } from 'lucide-vue-next'
import PageCard from '@/components/ui/PageCard.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
</script>

<template>
  <PageCard title="Preços e regras" subtitle="Configuração de cálculo, faixas PIX e cupons.">
    <UiEmptyState :icon="Calculator" title="Em construção" description="As regras de preço serão editadas aqui e enviadas ao crivonft em uma próxima etapa." />
  </PageCard>
</template>
