<script setup lang="ts">
/** The main white working surface of an admin page: title row with actions, then content. */
defineProps<{ title: string; subtitle?: string }>()
</script>

<template>
  <section class="min-h-full rounded-t-[var(--radius-card)] bg-surface px-4 pt-6 pb-10 sm:px-6 lg:rounded-[var(--radius-card)] lg:px-6 lg:pt-8">
    <header class="mb-6 flex flex-col gap-4 lg:mb-7 lg:flex-row lg:items-center lg:justify-between">
      <div class="min-w-0">
        <slot name="title">
          <h1 class="text-2xl font-semibold tracking-tight text-ink lg:text-[28px]">{{ title }}</h1>
        </slot>
        <p v-if="subtitle" class="mt-1 text-[15px] text-muted">{{ subtitle }}</p>
      </div>
      <div v-if="$slots.actions" class="flex flex-wrap items-center gap-3"><slot name="actions" /></div>
    </header>
    <slot />
  </section>
</template>
