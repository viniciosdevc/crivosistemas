<script setup lang="ts">
/** Busca de NFT: digita o nome, escolhe no autocomplete e vê foto, preços, lucro e margem pelas regras salvas. */
import { Search } from 'lucide-vue-next'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import PaymentIcon from '@/components/ui/PaymentIcon.vue'
import UiInput from '@/components/ui/UiInput.vue'
import { api } from '@/lib/api'
import { calculateFromUsd, dolarCompraParaCalculo, dolarFifo } from '@/lib/calculator'
import { useLotesUsd } from '@/lib/useLotesUsd'
import { formatInt, formatMoney, formatPercent } from '@/lib/format'
import type { NftCatalogo, NftItem, RegrasPrecificacao } from '@/lib/types'

const props = defineProps<{ regras: RegrasPrecificacao }>()

const busca = ref('')
const sugestoes = ref<NftItem[]>([])
const escolhido = ref<NftItem | null>(null)
const buscando = ref(false)
const falhou = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined
let controller: AbortController | undefined

watch(busca, (v) => {
  clearTimeout(timer)
  controller?.abort()
  if (escolhido.value && v === escolhido.value.nome) return
  escolhido.value = null
  if (v.trim().length < 2) {
    sugestoes.value = []
    buscando.value = false
    return
  }
  buscando.value = true
  timer = setTimeout(async () => {
    controller = new AbortController()
    try {
      falhou.value = false
      const res = await api.get<Pick<NftCatalogo, 'itens'>>('/api/nfts/busca', { q: v.trim() }, controller.signal)
      sugestoes.value = res.itens ?? []
    } catch (e) {
      if ((e as Error).name === 'AbortError') return
      falhou.value = true
      sugestoes.value = []
    }
    buscando.value = false
  }, 250)
})
onBeforeUnmount(() => {
  clearTimeout(timer)
  controller?.abort()
})

function escolher(item: NftItem) {
  escolhido.value = item
  busca.value = item.nome
  sugestoes.value = []
}

const lotes = useLotesUsd()
const calc = computed(() => {
  const cfg = props.regras.configuracao
  const item = escolhido.value
  if (!item) return null
  const r = calculateFromUsd(item.custoUsd, cfg, props.regras.faixasPix, dolarFifo(lotes.value, item.custoUsd, dolarCompraParaCalculo(cfg)))
  const k = Math.max(cfg.moedasPorK, 0.0001)
  const cambiosBrl = (r.precoVendaCambios * cfg.valorKFeira) / k
  const pct = (lucro: number) => (r.custoBrl > 0 ? (lucro / r.custoBrl) * 100 : 0)
  const lucroPix = r.precoVendaPix - r.custoBrl
  const lucroCambios = cambiosBrl - r.custoBrl
  return { r, cambiosBrl, lucroPix, lucroCambios, margemPix: pct(lucroPix), margemCambios: pct(lucroCambios) }
})
const tone = (v: number) => (v >= 15 ? 'text-success' : v >= 5 ? 'text-warning' : 'text-danger')
</script>

<template>
  <div class="grid gap-2.5">
    <div>
      <UiInput v-model="busca" label="Nome do NFT" placeholder="Digite para buscar…" autocomplete="off" role="combobox" :aria-expanded="sugestoes.length > 0">
        <template #prefix><Search class="size-4" /></template>
      </UiInput>
      <ul v-if="sugestoes.length" class="mt-2 grid gap-0.5 rounded-2xl border border-line bg-surface p-1" role="listbox">
        <li v-for="s in sugestoes.slice(0, 5)" :key="s.categoria + s.nome">
          <button type="button" class="flex w-full items-center gap-3 rounded-xl px-2.5 py-1 text-left hover:bg-surface-3" role="option" @click="escolher(s)">
            <img v-if="s.imagemUrl" :src="s.imagemUrl" :alt="s.nome" class="size-8 shrink-0 rounded-lg bg-surface-3 object-contain" loading="lazy" />
            <span v-else class="size-8 shrink-0 rounded-lg bg-surface-3" />
            <span class="min-w-0 flex-1 truncate text-sm font-medium text-ink">{{ s.nome }}</span>
            <span class="text-xs text-muted tabular">$ {{ s.custoUsd.toFixed(2) }}</span>
          </button>
        </li>
      </ul>
    </div>

    <p v-if="falhou" class="rounded-xl bg-warning-soft px-3 py-2 text-[13px] text-warning">Não foi possível consultar o catálogo agora.</p>
    <p v-else-if="buscando" class="text-sm text-muted">Buscando…</p>
    <p v-else-if="!escolhido && busca.trim().length >= 2 && !sugestoes.length" class="text-sm text-muted">Nenhum NFT encontrado.</p>

    <div v-if="escolhido && calc" class="grid gap-2 text-sm" aria-live="polite">
      <div class="flex items-center gap-3">
        <img v-if="escolhido.imagemUrl" :src="escolhido.imagemUrl" :alt="escolhido.nome" class="size-12 shrink-0 rounded-xl bg-surface-3 object-contain" />
        <div class="min-w-0">
          <p class="truncate text-base font-semibold text-ink">{{ escolhido.nome }}</p>
          <p class="text-muted">Custo $ {{ escolhido.custoUsd.toFixed(2) }} · {{ formatMoney(calc.r.custoBrl) }} · {{ formatInt(calc.r.custoCambios) }} C</p>
        </div>
      </div>

      <table class="w-full">
        <thead>
          <tr class="bg-surface-3 text-left text-xs uppercase tracking-wide text-muted">
            <th class="rounded-l-xl px-3 py-1 font-medium">Pagamento</th>
            <th class="px-3 py-2 text-right font-medium">Preço</th>
            <th class="px-3 py-2 text-right font-medium">Lucro</th>
            <th class="rounded-r-xl px-3 py-2 text-right font-medium">Margem</th>
          </tr>
        </thead>
        <tbody class="tabular">
          <tr class="border-b border-line">
            <td class="px-3 py-1.5"><span class="inline-flex items-center gap-1.5 text-ink"><PaymentIcon kind="cambios" class="size-4" />Câmbios</span></td>
            <td class="px-3 py-1.5 text-right text-ink">{{ formatInt(calc.r.precoVendaCambios) }} C <span class="text-xs text-muted">({{ formatMoney(calc.cambiosBrl) }})</span></td>
            <td class="px-3 py-1.5 text-right font-semibold text-ink">{{ formatMoney(calc.lucroCambios) }}</td>
            <td :class="['px-3 py-1.5 text-right font-semibold', tone(calc.margemCambios)]">{{ formatPercent(calc.margemCambios) }}</td>
          </tr>
          <tr>
            <td class="px-3 py-1.5"><span class="inline-flex items-center gap-1.5 text-ink"><PaymentIcon kind="pix" class="size-4" />PIX</span></td>
            <td class="px-3 py-1.5 text-right text-ink">{{ formatMoney(calc.r.precoVendaPix) }}</td>
            <td class="px-3 py-1.5 text-right font-semibold text-ink">{{ formatMoney(calc.lucroPix) }}</td>
            <td :class="['px-3 py-1.5 text-right font-semibold', tone(calc.margemPix)]">{{ formatPercent(calc.margemPix) }}</td>
          </tr>
        </tbody>
      </table>
      <p class="text-xs text-muted">Margem sobre o custo (com taxa de prata). Dólar de compra {{ formatMoney(calc.r.dolarCompra) }}, K da feira {{ formatMoney(regras.configuracao.valorKFeira) }}.</p>
    </div>
  </div>
</template>
