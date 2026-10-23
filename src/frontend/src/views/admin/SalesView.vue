<script setup lang="ts">
import { ReceiptText } from 'lucide-vue-next'
import PageCard from '@/components/ui/PageCard.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
</script>

<template>
  <PageCard title="Vendas" subtitle="Vendas de NFTs e câmbios.">
    <UiEmptyState :icon="ReceiptText" title="Em construção" description="A lista de vendas vem da integração com o crivonft e será liberada em uma próxima etapa." />
  </PageCard>
</template>
