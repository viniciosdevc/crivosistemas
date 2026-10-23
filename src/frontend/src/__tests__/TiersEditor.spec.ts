import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import TiersEditor from '@/components/admin/pricing/TiersEditor.vue'
import { toRows, type TierRow } from '@/lib/pricing'

const start = () =>
  toRows([
    { valorMinUsd: 0, valorMaxUsd: 10, multiplicador: 6.2, ordem: 0, ativo: true },
    { valorMinUsd: 10.01, valorMaxUsd: null, multiplicador: 6, ordem: 1, ativo: true },
  ])

function mountEditor() {
  const model = ref<TierRow[]>(start())
  const wrapper = mount(TiersEditor, {
    props: { modelValue: model.value, 'onUpdate:modelValue': (v: TierRow[]) => { model.value = v; void wrapper.setProps({ modelValue: v }) } },
    global: { stubs: { RouterLink: true } },
  })
  return { wrapper, model }
}

describe('TiersEditor', () => {
  it('mostra uma linha por faixa', () => {
    const { wrapper } = mountEditor()
    expect(wrapper.findAll('[data-testid="tier-row"]')).toHaveLength(2)
  })

  it('adiciona uma faixa depois da última', async () => {
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })
    const { wrapper, model } = mountEditor()
    await wrapper.findAll('button').find((b) => b.text().includes('Adicionar faixa'))!.trigger('click')
    expect(model.value).toHaveLength(3)
    // A última faixa estava sem limite: a nova começa 10 dólares acima do mínimo dela.
    expect(model.value[2]!.min).toBe('20,01')
  })

  it('remove uma faixa', async () => {
    const { wrapper, model } = mountEditor()
    await wrapper.find('[aria-label="Remover faixa 1"]').trigger('click')
    expect(model.value).toHaveLength(1)
    expect(model.value[0]!.min).toBe('10,01')
  })

  it('reordena para baixo e para cima; os extremos ficam desabilitados', async () => {
    const { wrapper, model } = mountEditor()
    expect(wrapper.find('[aria-label="Subir faixa 1"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[aria-label="Descer faixa 2"]').attributes('disabled')).toBeDefined()
    await wrapper.find('[aria-label="Descer faixa 1"]').trigger('click')
    expect(model.value.map((r) => r.min)).toEqual(['10,01', '0'])
    await wrapper.find('[aria-label="Subir faixa 2"]').trigger('click')
    expect(model.value.map((r) => r.min)).toEqual(['0', '10,01'])
  })

  it('mostra erro de campo inválido e aviso de sobreposição', async () => {
    const { wrapper, model } = mountEditor()
    model.value = model.value.map((r, i) => (i === 0 ? { ...r, max: '0' } : r))
    await wrapper.setProps({ modelValue: model.value })
    expect(wrapper.text()).toContain('Precisa ser maior que o mínimo.')
  })
})
