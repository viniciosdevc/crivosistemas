<script setup lang="ts">
import { Users } from 'lucide-vue-next'
import PageCard from '@/components/ui/PageCard.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
</script>

<template>
  <PageCard title="Clientes" subtitle="Quem compra de você.">
    <UiEmptyState :icon="Users" title="Em construção" description="O cadastro de clientes vem da integração com o crivonft e será liberado em uma próxima etapa." />
  </PageCard>
</template>
