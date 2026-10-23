<script setup lang="ts">
/** Tela de acesso dividida: formulário à esquerda, prévia do produto à direita (só no desktop). */
import { Calculator, ChartColumn, Plug, ReceiptText, Users } from 'lucide-vue-next'
import { onMounted, onUnmounted, ref } from 'vue'
import BrandMark from '@/components/BrandMark.vue'

const slides = [
  { title: ['Suas vendas de NFT e câmbio,', 'num só lugar'], text: 'Vendas, clientes e lucro sempre à mão.' },
  { title: ['Preços e regras', 'sob seu controle'], text: 'Faixas PIX, cálculo e cupons com um único cadastro.' },
  { title: ['Integrado ao crivonft,', 'sem retrabalho'], text: 'As vendas chegam sozinhas, as regras seguem para a loja.' },
]
const slide = ref(0)
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => (timer = setInterval(() => (slide.value = (slide.value + 1) % slides.length), 6000)))
onUnmounted(() => clearInterval(timer))
</script>

<template>
  <div class="relative flex min-h-dvh bg-surface">
    <div class="dot-pattern pointer-events-none absolute bottom-0 left-0 h-64 w-1/2 opacity-60 [mask-image:linear-gradient(to_top,black,transparent)]" />
    <div class="relative flex flex-1 items-center justify-center px-5 py-12">
      <div class="w-full max-w-[400px]">
        <div class="mb-8 flex flex-col items-center text-center">
          <BrandMark class="mb-6" />
          <slot name="header" />
        </div>
        <slot />
      </div>
    </div>

    <div class="hidden flex-1 p-4 lg:flex">
      <div class="relative flex flex-1 flex-col overflow-hidden rounded-[32px] bg-canvas">
        <!-- Prévia do produto, feita com a própria linguagem visual do app -->
        <div class="flex flex-1 gap-4 overflow-hidden pt-8 pl-8" aria-hidden="true">
          <div class="flex w-14 shrink-0 flex-col items-center gap-3 rounded-2xl bg-surface py-4">
            <BrandMark compact />
            <span class="mt-2 rounded-xl bg-brand p-2 text-brand-ink"><ChartColumn class="size-5" /></span>
            <span class="p-2 text-muted"><ReceiptText class="size-5" /></span>
            <span class="p-2 text-muted"><Users class="size-5" /></span>
            <span class="p-2 text-muted"><Calculator class="size-5" /></span>
            <span class="p-2 text-muted"><Plug class="size-5" /></span>
          </div>
          <div class="flex-1 rounded-tl-3xl bg-surface p-7">
            <div class="mb-6 flex gap-2">
              <span v-for="(p, i) in ['Hoje', 'Este mês', 'Últimos 30 dias']" :key="p" :class="['rounded-full border px-4 py-2 text-sm', i === 1 ? 'border-[#b9e98f] bg-brand-soft text-ink' : 'border-line text-text']">{{ p }}</span>
            </div>
            <div class="grid grid-cols-2 gap-6 border-b border-line pb-6">
              <div>
                <p class="text-sm text-muted">Vendas</p>
                <p class="mt-1 text-[34px] font-semibold tracking-tight text-ink"><span class="text-[28px] text-muted">R$</span> 8.179<span class="text-sm text-muted">,00</span></p>
                <p class="mt-1 text-sm font-medium text-success">↗ +12%</p>
              </div>
              <div class="border-l border-line pl-6">
                <p class="text-sm text-muted">Lucro</p>
                <p class="mt-1 text-[34px] font-semibold tracking-tight text-ink"><span class="text-[28px] text-muted">R$</span> 956<span class="text-sm text-muted">,40</span></p>
                <p class="mt-1 text-sm text-muted">margem 11%</p>
              </div>
            </div>
            <div class="mt-6 rounded-2xl border border-line">
              <div v-for="(o, i) in [['NFT', 'Ana Ribeiro', 'Pago'], ['Câmbios', 'Carlos M.', 'Pendente'], ['NFT', 'Juliana S.', 'Concluído']]" :key="o[0]" :class="['flex items-center justify-between px-5 py-4 text-sm', i > 0 && 'border-t border-line']">
                <span class="text-muted">{{ o[0] }}</span>
                <span class="text-ink">{{ o[1] }}</span>
                <span :class="['rounded-md px-2 py-1 text-xs', i === 2 ? 'bg-success-soft text-success' : i === 1 ? 'bg-warning-soft text-warning' : 'bg-brand-soft text-[#3d7a1c]']">{{ o[2] }}</span>
              </div>
            </div>
          </div>
        </div>
        <div class="relative -mt-40 bg-gradient-to-t from-canvas via-canvas to-transparent px-10 pt-36 pb-10 text-center">
          <Transition mode="out-in" enter-from-class="opacity-0" enter-active-class="transition duration-300" leave-to-class="opacity-0" leave-active-class="transition duration-200">
            <div :key="slide">
              <p class="text-[30px] leading-tight font-semibold tracking-tight">
                <span class="text-[#5aa63a]">{{ slides[slide]!.title[0] }}</span><br />
                <span class="text-ink">{{ slides[slide]!.title[1] }}</span>
              </p>
              <p class="mt-3 text-[15px] text-text">{{ slides[slide]!.text }}</p>
            </div>
          </Transition>
          <div class="mt-6 flex justify-center gap-1.5">
            <button
              v-for="(_, i) in slides"
              :key="i"
              type="button"
              :aria-label="`Mensagem ${i + 1}`"
              :class="['h-2 rounded-full transition-all', i === slide ? 'w-8 bg-brand' : 'w-2 bg-line-strong']"
              @click="slide = i"
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
