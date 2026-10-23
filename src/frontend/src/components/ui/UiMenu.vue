<script setup lang="ts">
import { EllipsisVertical } from 'lucide-vue-next'
import { DropdownMenuContent, DropdownMenuItem, DropdownMenuPortal, DropdownMenuRoot, DropdownMenuSeparator, DropdownMenuTrigger } from 'reka-ui'
import type { Component } from 'vue'

export interface MenuItem {
  label: string
  icon?: Component
  danger?: boolean
  disabled?: boolean
  separatorBefore?: boolean
  onSelect: () => void
}

defineProps<{ items: MenuItem[]; label?: string }>()
</script>

<template>
  <DropdownMenuRoot :modal="false">
    <DropdownMenuTrigger as-child>
      <slot name="trigger">
        <button
          type="button"
          class="inline-flex size-9 items-center justify-center rounded-full text-muted hover:bg-surface-3 hover:text-ink data-[state=open]:bg-surface-3"
          :aria-label="label ?? 'Mais ações'"
          @click.stop
        >
          <EllipsisVertical class="size-[18px]" />
        </button>
      </slot>
    </DropdownMenuTrigger>
    <DropdownMenuPortal>
      <DropdownMenuContent
        align="end"
        :side-offset="6"
        class="z-50 min-w-52 rounded-2xl border border-line bg-surface p-1.5 shadow-[0_16px_40px_-12px_rgb(0_0_0/0.18)] data-[state=open]:animate-fade-in"
      >
        <template v-for="item in items" :key="item.label">
          <DropdownMenuSeparator v-if="item.separatorBefore" class="my-1 h-px bg-line" />
          <DropdownMenuItem
            :disabled="item.disabled"
            :class="[
              'flex cursor-pointer items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm outline-none select-none data-[disabled]:cursor-not-allowed data-[disabled]:opacity-40',
              item.danger ? 'text-danger data-[highlighted]:bg-danger-soft' : 'text-text data-[highlighted]:bg-surface-3 data-[highlighted]:text-ink',
            ]"
            @select="item.onSelect"
          >
            <component :is="item.icon" v-if="item.icon" class="size-4" />
            {{ item.label }}
          </DropdownMenuItem>
        </template>
      </DropdownMenuContent>
    </DropdownMenuPortal>
  </DropdownMenuRoot>
</template>
