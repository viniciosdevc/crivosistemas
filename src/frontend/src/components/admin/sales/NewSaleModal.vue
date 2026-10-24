<script setup lang="ts">
import NewSaleForm from '@/components/admin/sales/NewSaleForm.vue'
import UiModal from '@/components/ui/UiModal.vue'
import type { VendaLista } from '@/lib/types'

const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ created: [venda: VendaLista] }>()

function onCreated(venda: VendaLista) {
  open.value = false
  emit('created', venda)
}
</script>

<template>
  <UiModal v-model:open="open" title="Nova venda" description="Lance uma venda feita fora do site. O custo, o lucro e a margem são recalculados pelo servidor." size="xl" compact>
    <NewSaleForm v-if="open" @created="onCreated" @cancel="open = false" />
  </UiModal>
</template>
