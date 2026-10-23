<script setup lang="ts">
import UpdateBanner from '@/components/UpdateBanner.vue'
import ConfirmHost from '@/components/ui/ConfirmHost.vue'
import ToastHost from '@/components/ui/ToastHost.vue'
</script>

<template>
  <RouterView />
  <ToastHost />
  <ConfirmHost />
  <UpdateBanner />
</template>
