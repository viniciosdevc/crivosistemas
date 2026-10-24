<script setup lang="ts">
/** Ícones simples de forma de pagamento: PIX (losango) e Câmbios (moeda). Herdam a cor do texto. */
defineProps<{ kind: 'pix' | 'cambios' }>()
</script>

<template>
  <svg v-if="kind === 'pix'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-4 shrink-0" aria-hidden="true" data-icon="pix">
    <path d="M12 3.5 20.5 12 12 20.5 3.5 12Z" />
    <path d="M8.5 12h7M12 8.5v7" />
  </svg>
  <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-4 shrink-0" aria-hidden="true" data-icon="cambios">
    <circle cx="12" cy="12" r="8.5" />
    <path d="m12 7.5 3.5 4.5-3.5 4.5L8.5 12Z" />
  </svg>
</template>
