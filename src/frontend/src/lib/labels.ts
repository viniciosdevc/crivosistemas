export type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand'

export const userRole: Record<string, { label: string; tone: Tone }> = {
  admin: { label: 'Administrador', tone: 'brand' },
  operator: { label: 'Operador', tone: 'neutral' },
}

export const userStatus: Record<string, { label: string; tone: Tone }> = {
  active: { label: 'Ativo', tone: 'success' },
  disabled: { label: 'Desativado', tone: 'danger' },
}

export const origemVenda: Record<string, { label: string; tone: Tone }> = {
  site: { label: 'Site', tone: 'info' },
  nft: { label: 'NFT', tone: 'brand' },
  cambios: { label: 'Câmbios', tone: 'neutral' },
  manual: { label: 'Manual', tone: 'warning' },
}

export const statusVenda: Record<string, { label: string; tone: Tone }> = {
  pendente: { label: 'Pendente', tone: 'warning' },
  pago: { label: 'Pago', tone: 'success' },
  concluido: { label: 'Concluído', tone: 'success' },
  cancelado: { label: 'Cancelado', tone: 'danger' },
  reembolsado: { label: 'Reembolsado', tone: 'neutral' },
}

export const formaPagamento: Record<string, { label: string; tone: Tone }> = {
  pix: { label: 'PIX', tone: 'neutral' },
  cambios: { label: 'Câmbios', tone: 'neutral' },
  hibrido: { label: 'Híbrido', tone: 'neutral' },
}

export const origemCusto: Record<string, string> = {
  estoque_usd: 'Estoque em USD',
  cambios: 'Comprado em Câmbios',
  estoque_esmeralda: 'Estoque de Esmeraldas',
  manual: 'Custo Manual',
}

export const origemLote: Record<string, { label: string; tone: Tone }> = {
  compra: { label: 'Compra', tone: 'success' },
  ajuste: { label: 'Ajuste', tone: 'warning' },
  importacao_crivonft: { label: 'Importado do crivonft', tone: 'info' },
}

export const motivoMovimento: Record<string, { label: string; tone: Tone }> = {
  compra: { label: 'Compra', tone: 'success' },
  venda: { label: 'Venda', tone: 'info' },
  estorno_venda: { label: 'Estorno de venda', tone: 'neutral' },
  ajuste: { label: 'Ajuste', tone: 'warning' },
  cancelamento: { label: 'Cancelamento', tone: 'danger' },
  importacao: { label: 'Importação', tone: 'info' },
  edicao: { label: 'Correção de compra', tone: 'neutral' },
}

export const tipoMovimentoCambios: Record<string, { label: string; tone: Tone }> = {
  venda: { label: 'Venda de câmbios', tone: 'info' },
  venda_manual: { label: 'Recebido em venda', tone: 'success' },
  ajuste: { label: 'Ajuste de saldo', tone: 'warning' },
  estorno: { label: 'Estorno', tone: 'neutral' },
}

export const tipoCupom: Record<string, string> = {
  percentual: 'Percentual',
  valor_fixo: 'Valor fixo',
  taxa_gratis: 'Taxa grátis',
}

export const estadoEvento: Record<string, { label: string; tone: Tone }> = {
  recebido: { label: 'Recebido', tone: 'info' },
  processado: { label: 'Processado', tone: 'success' },
  falhou: { label: 'Falhou', tone: 'danger' },
  rejeitado: { label: 'Rejeitado', tone: 'warning' },
}

export const estadoReconciliacao: Record<string, { label: string; tone: Tone }> = {
  pendente: { label: 'Na fila', tone: 'warning' },
  executando: { label: 'Executando', tone: 'info' },
  concluida: { label: 'Concluída', tone: 'success' },
  falhou: { label: 'Falhou', tone: 'danger' },
}

export const tipoReconciliacao: Record<string, string> = {
  noturna: 'Noturna',
  manual: 'Manual',
  carga_inicial: 'Carga inicial',
}

export const estadoEnvio: Record<string, { label: string; tone: Tone }> = {
  pendente: { label: 'Na fila', tone: 'warning' },
  enviado: { label: 'Enviado', tone: 'success' },
  falhou: { label: 'Falhou', tone: 'danger' },
  substituido: { label: 'Substituído', tone: 'neutral' },
}

export const origemHistorico: Record<string, string> = {
  painel: 'Painel',
  importacao_crivonft: 'Importação do crivonft',
}

export const tipoEvento: Record<string, string> = {
  'venda.upsert': 'Venda criada ou atualizada',
  'venda.removida': 'Venda removida',
  'cliente.upsert': 'Cliente criado ou atualizado',
}

/** Rótulo de um valor conhecido, ou o próprio valor quando o backend mandar algo novo. */
export function labelOf(map: Record<string, { label: string; tone: Tone }>, value: string | null | undefined) {
  if (!value) return { label: '—', tone: 'neutral' as Tone }
  return map[value] ?? { label: value, tone: 'neutral' as Tone }
}
