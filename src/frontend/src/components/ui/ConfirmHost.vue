<script setup lang="ts">
import { confirmState, resolveConfirm } from '@/lib/confirm'
import UiButton from './UiButton.vue'
import UiModal from './UiModal.vue'
import UiTextarea from './UiTextarea.vue'
</script>

<template>
  <UiModal
    :open="confirmState.open"
    :title="confirmState.title"
    :description="confirmState.description"
    size="sm"
    @update:open="(v) => !v && resolveConfirm(false)"
  >
    <UiTextarea v-if="confirmState.reasonLabel" v-model="confirmState.reason" :label="confirmState.reasonLabel" maxlength="300" />
    <template #footer>
      <UiButton variant="subtle" @click="resolveConfirm(false)">{{ confirmState.cancelLabel }}</UiButton>
      <UiButton :variant="confirmState.danger ? 'danger' : 'primary'" @click="resolveConfirm(true)">{{ confirmState.confirmLabel }}</UiButton>
    </template>
  </UiModal>
</template>
