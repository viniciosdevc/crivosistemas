<script setup lang="ts">
/** Editor das faixas PIX: adicionar, remover, reordenar e ativar. Os campos são texto para aceitar vírgula. */
import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-vue-next'
import { computed } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiSwitch from '@/components/ui/UiSwitch.vue'
import { addTier, moveTier, removeTier, validateTiers, type TierRow } from '@/lib/pricing'

const rows = defineModel<TierRow[]>({ required: true })

const validation = computed(() => validateTiers(rows.value))
const update = (i: number, patch: Partial<TierRow>) => (rows.value = rows.value.map((r, k) => (k === i ? { ...r, ...patch } : r)))
</script>

<template>
  <div>
    <ol class="space-y-3" aria-label="Faixas PIX">
      <li v-for="(row, i) in rows" :key="row.key" class="rounded-2xl border border-line p-4" :class="!row.ativo && 'bg-surface-2'" data-testid="tier-row">
        <div class="flex items-center justify-between gap-3">
          <span class="text-sm font-medium text-ink">Faixa {{ i + 1 }}</span>
          <div class="flex items-center gap-1">
            <UiSwitch :model-value="row.ativo" :label="row.ativo ? 'Ativa' : 'Inativa'" class="mr-2 items-center gap-3" @update:model-value="(v) => update(i, { ativo: v })" />
            <UiButton variant="ghost" size="icon-sm" :disabled="i === 0" :aria-label="`Subir faixa ${i + 1}`" @click="rows = moveTier(rows, i, -1)"><ArrowUp class="size-4" /></UiButton>
            <UiButton variant="ghost" size="icon-sm" :disabled="i === rows.length - 1" :aria-label="`Descer faixa ${i + 1}`" @click="rows = moveTier(rows, i, 1)"><ArrowDown class="size-4" /></UiButton>
            <UiButton variant="ghost" size="icon-sm" :aria-label="`Remover faixa ${i + 1}`" @click="rows = removeTier(rows, i)"><Trash2 class="size-4 text-danger" /></UiButton>
          </div>
        </div>
        <div class="mt-3 grid gap-3 sm:grid-cols-3">
          <UiInput label="De (USD)" prefix="$" inputmode="decimal" :model-value="row.min" :error="validation.errors[i]?.min" @update:model-value="(v) => update(i, { min: String(v ?? '') })" />
          <UiInput label="Até (USD)" prefix="$" inputmode="decimal" placeholder="Sem limite" :model-value="row.max" :error="validation.errors[i]?.max" @update:model-value="(v) => update(i, { max: String(v ?? '') })" />
          <UiInput label="Multiplicador" suffix="×" inputmode="decimal" :model-value="row.mult" :error="validation.errors[i]?.mult" @update:model-value="(v) => update(i, { mult: String(v ?? '') })" />
        </div>
      </li>
    </ol>
    <p v-if="!rows.length" class="rounded-2xl border border-dashed border-line-strong px-4 py-8 text-center text-muted">
      Nenhuma faixa. Sem faixas ativas, o cálculo usa as faixas padrão do crivonft.
    </p>
    <ul v-if="validation.warnings.length" class="mt-3 space-y-1 text-[13px] text-warning" role="status">
      <li v-for="w in validation.warnings" :key="w">{{ w }}</li>
    </ul>
    <UiButton variant="secondary" class="mt-4" @click="rows = addTier(rows)"><Plus class="size-4" /> Adicionar faixa</UiButton>
  </div>
</template>
