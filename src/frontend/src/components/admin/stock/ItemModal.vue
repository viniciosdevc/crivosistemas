<script setup lang="ts">
import { Search } from 'lucide-vue-next'
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { formatInt, formatMoney, formatUsd, parseDecimal, todayIso } from '@/lib/format'
import { analisarItem } from '@/lib/itens'
import { toast } from '@/lib/toast'
import type { ItemEstoque, NftCatalogo, NftItem, RegrasPrecificacao } from '@/lib/types'

/** Cadastra um item: busca o nome no catálogo (foto e preço de mercado), informa quanto pagou e quantas unidades. */
const props = defineProps<{ regras: RegrasPrecificacao | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [item: ItemEstoque] }>()

const form = reactive({ data: '', quantidade: '1', custo: '' })
const busca = ref('')
const sugestoes = ref<NftItem[]>([])
const escolhido = ref<NftItem | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})
let timer: ReturnType<typeof setTimeout> | undefined
let controller: AbortController | undefined

watch(open, (v) => {
  if (!v) return
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
  Object.assign(form, { data: todayIso(), quantidade: '1', custo: '' })
  busca.value = ''
  sugestoes.value = []
  escolhido.value = null
})

watch(busca, (v) => {
  clearTimeout(timer)
  controller?.abort()
  if (escolhido.value && v === escolhido.value.nome) return
  escolhido.value = null
  if (v.trim().length < 2) { sugestoes.value = []; return }
  timer = setTimeout(async () => {
    controller = new AbortController()
    try {
      const res = await api.get<Pick<NftCatalogo, 'itens'>>('/api/nfts/busca', { q: v.trim() }, controller.signal)
      sugestoes.value = (res.itens ?? []).slice(0, 5)
    } catch (e) {
      if ((e as Error).name !== 'AbortError') sugestoes.value = []
    }
  }, 250)
})
onBeforeUnmount(() => { clearTimeout(timer); controller?.abort() })

function escolher(item: NftItem) {
  escolhido.value = item
  busca.value = item.nome
  sugestoes.value = []
}

const quantidade = computed(() => Math.trunc(parseDecimal(form.quantidade) ?? 0))
const custo = computed(() => parseDecimal(form.custo))
const ready = computed(() => busca.value.trim().length > 0 && !!form.data && quantidade.value >= 1 && custo.value != null && custo.value >= 0)
const analise = computed(() => {
  const item = escolhido.value
  if (!props.regras || custo.value == null || quantidade.value < 1) return null
  return analisarItem({ custoUnitario: custo.value, precoMercadoUsd: item?.custoUsd ?? null, quantidade: quantidade.value }, props.regras)
})

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    const saved = await api.post<ItemEstoque>('/api/estoque/itens', {
      nome: busca.value.trim(),
      categoria: escolhido.value?.categoria ?? null,
      quantidade: quantidade.value,
      custoUnitario: custo.value ?? 0,
      data: form.data,
    })
    toast.success('Item adicionado ao estoque')
    open.value = false
    emit('saved', saved)
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível salvar o item.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Adicionar item" description="Busque o NFT pelo nome: a foto e o preço de mercado vêm do catálogo.">
    <form id="item-form" class="flex flex-col gap-4" novalidate @submit.prevent="submit">
      <div>
        <UiInput v-model="busca" label="Nome do item" placeholder="Digite para buscar…" autocomplete="off" :error="fieldErrors.nome">
          <template #prefix><Search class="size-4" /></template>
        </UiInput>
        <ul v-if="sugestoes.length" class="mt-2 grid gap-0.5 rounded-2xl border border-line bg-surface p-1" role="listbox">
          <li v-for="s in sugestoes" :key="s.categoria + s.nome">
            <button type="button" class="flex w-full items-center gap-3 rounded-xl px-2.5 py-1 text-left hover:bg-surface-3" role="option" @click="escolher(s)">
              <img v-if="s.imagemUrl" :src="s.imagemUrl" :alt="s.nome" class="size-8 shrink-0 rounded-lg bg-surface-3 object-contain" loading="lazy" />
              <span v-else class="size-8 shrink-0 rounded-lg bg-surface-3" />
              <span class="min-w-0 flex-1 truncate text-sm font-medium text-ink">{{ s.nome }}</span>
              <span class="text-xs text-muted tabular">{{ formatUsd(s.custoUsd) }}</span>
            </button>
          </li>
        </ul>
        <div v-if="escolhido" class="mt-3 flex items-center gap-3 text-sm">
          <img v-if="escolhido.imagemUrl" :src="escolhido.imagemUrl" :alt="escolhido.nome" class="size-12 shrink-0 rounded-xl bg-surface-3 object-contain" />
          <p class="text-muted">Mercado <span class="font-semibold text-ink tabular">{{ formatUsd(escolhido.custoUsd) }}</span> por unidade</p>
        </div>
      </div>
      <div class="grid grid-cols-2 gap-3">
        <UiInput v-model="form.custo" label="Valor pago (cada)" prefix="US$" inputmode="decimal" placeholder="0,00" :error="fieldErrors.custoUnitario" />
        <UiInput v-model="form.quantidade" label="Quantidade" inputmode="numeric" :error="fieldErrors.quantidade" />
      </div>
      <UiDatePicker v-model="form.data" label="Data da compra" :max="todayIso()" :error="fieldErrors.data" />
      <div v-if="analise" class="grid gap-1 text-sm text-muted">
        <p>Custo <span class="font-semibold text-ink tabular">{{ formatInt(analise.custoCambios) }} C</span> · <span class="tabular">{{ formatMoney(analise.custoBrl) }}</span></p>
        <template v-if="analise.mercado">
          <p>Diferença vs mercado <span :class="['font-semibold tabular', analise.mercado.diferencaUsd >= 0 ? 'text-success' : 'text-danger']">{{ formatUsd(analise.mercado.diferencaUsd) }}</span></p>
          <p>
            Lucro potencial
            <span :class="['font-semibold tabular', analise.mercado.lucroPix >= 0 ? 'text-success' : 'text-danger']">{{ formatMoney(analise.mercado.lucroPix) }} no PIX</span>
            ·
            <span :class="['font-semibold tabular', analise.mercado.lucroCambios >= 0 ? 'text-success' : 'text-danger']">{{ formatInt(analise.mercado.lucroCambios) }} C</span>
          </p>
        </template>
      </div>
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="item-form" size="lg" :loading="loading" :disabled="!ready">Adicionar</UiButton>
    </template>
  </UiModal>
</template>
