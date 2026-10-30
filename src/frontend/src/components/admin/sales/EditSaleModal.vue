<script setup lang="ts">
import NewSaleForm from '@/components/admin/sales/NewSaleForm.vue'
import UiModal from '@/components/ui/UiModal.vue'
import type { VendaDetalhe, VendaLista } from '@/lib/types'

defineProps<{ venda: VendaDetalhe }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [venda: VendaLista] }>()

function onSaved(venda: VendaLista) {
  open.value = false
  emit('saved', venda)
}
</script>

<template>
  <UiModal v-model:open="open" title="Editar venda" description="Os valores abaixo substituem os da venda; o servidor recalcula custo, lucro e margem." size="md" compact>
    <NewSaleForm v-if="open" :venda="venda" @saved="onSaved" @cancel="open = false" />
  </UiModal>
</template>
