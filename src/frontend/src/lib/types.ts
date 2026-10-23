/** Tipos que espelham os DTOs da API (camelCase). */

export type Role = 'admin' | 'operator'
export type UserStatus = 'active' | 'disabled'

export interface Me {
  id: string
  name: string
  email: string
  role: Role
}

export interface AppUser {
  id: string
  name: string
  email: string
  role: Role
  status: UserStatus
  createdAt: string
  lastLoginAt: string | null
  isYou: boolean
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

// ── Vendas e clientes ───────────────────────────────────────────────────────

export type Origem = 'nft' | 'cambios' | 'site'
export type StatusVenda = 'pendente' | 'pago' | 'concluido' | 'cancelado' | 'reembolsado'
export type FormaPagamento = 'pix' | 'cambios' | 'hibrido'

export interface ClienteResumo {
  id: string
  nickname: string
  email: string | null
}

export interface VendaLista {
  id: string
  origem: Origem
  origemId: string
  pedidoId: string | null
  dataHora: string
  /** Dia de calendário em America/Sao_Paulo (AAAA-MM-DD). */
  dia: string
  status: string
  statusNormalizado: StatusVenda
  formaPagamento: FormaPagamento | null
  vendedor: string | null
  cliente: ClienteResumo | null
  totalBrl: number
  lucroBrl: number
  margemPercent: number
  removida: boolean
  faturavel: boolean
}

export interface VendaItem {
  ordem: number
  descricao: string
  quantidade: number
  precoUsd: number
  precoBrl: number
  referencia: string | null
}

export interface Valores {
  totalBrl: number
  descontoBrl: number
  taxaBrl: number
  valorPixBrl: number
  valorCambios: number
  custoBrl: number
  custoUsd: number
  custoCambios: number
  lucroBrl: number
  margemPercent: number
}

export interface Cupom {
  codigo: string
  tipo: 'percentual' | 'valor_fixo' | 'taxa_gratis' | null
}

export type EstadoEvento = 'recebido' | 'processado' | 'falhou' | 'rejeitado'

export interface VendaEvento {
  id: string
  eventoId: string
  tipo: string
  estado: EstadoEvento
  ocorridoEm: string
  recebidoEm: string
  resultado: string | null
  erro: string | null
}

export interface VendaDetalhe {
  venda: VendaLista
  valores: Valores
  cupom: Cupom | null
  itens: VendaItem[]
  atualizadoEmOrigem: string
  removidaEm: string | null
  eventos: VendaEvento[]
}

export interface ResumoTotais {
  faturamento: number
  lucro: number
  ticketMedio: number
  quantidade: number
}

export interface ResumoOrigem {
  origem: Origem
  faturamento: number
  lucro: number
  quantidade: number
}

export interface ResumoDia {
  dia: string
  faturamento: number
  lucro: number
  quantidade: number
}

export interface ResumoVendas {
  de: string
  ate: string
  totais: ResumoTotais
  porOrigem: ResumoOrigem[]
  porDia: ResumoDia[]
}

export interface ClienteLista {
  id: string
  nickname: string
  email: string | null
  totalGasto: number
  compras: number
  ultimaCompra: string | null
}

export interface ClienteDetalhe {
  cliente: ClienteLista
  chave: string
  identidadeId: string | null
  criadoEmOrigem: string | null
  vendas: VendaLista[]
}

// ── Precificação ────────────────────────────────────────────────────────────

export interface ConfigCalculo {
  valorKFeira: number
  valorKTrocas: number
  moedasPorK: number
  dolarPadraoCompra: number
  dolarPadraoVenda: number
  taxaPrata: number
  margemVendaCambiosPercent: number
}

export interface FaixaPix {
  valorMinUsd: number
  valorMaxUsd: number | null
  multiplicador: number
  ordem: number
  ativo: boolean
}

export type EstadoEnvio = 'pendente' | 'enviado' | 'falhou' | 'substituido'

export interface EnvioPrecificacao {
  id: string
  versao: number
  estado: EstadoEnvio
  tentativas: number
  criadoEm: string
  ultimaTentativaEm: string | null
  proximaTentativaEm: string | null
  enviadoEm: string | null
  ultimoStatusHttp: number | null
  ultimoErro: string | null
}

export interface Precificacao {
  versao: number
  configuracao: ConfigCalculo
  faixasPix: FaixaPix[]
  ultimoEnvio: EnvioPrecificacao | null
  envioConfigurado: boolean
}

export interface SalvarPrecificacao {
  versao: number
  configuracao: ConfigCalculo
  faixasPix: FaixaPix[]
}

/** O histórico guarda o corpo do contrato do crivonft, em snake_case. */
export interface PayloadPrecificacao {
  versao: number
  configuracao_calculo: {
    valor_k_feira: number
    valor_k_trocas: number
    moedas_por_k: number
    dolar_padrao_compra: number
    dolar_padrao_venda: number
    taxa_prata: number
    margem_venda_cambios_percent: number
  }
  faixas_pix: { valor_min_usd: number; valor_max_usd: number | null; multiplicador: number; ordem: number; ativo: boolean }[]
}

export interface HistoricoPrecificacao {
  id: string
  versao: number
  userNome: string
  origem: string
  criadoEm: string
  antes: PayloadPrecificacao
  depois: PayloadPrecificacao
}

export interface CalculoPreco {
  precoUsd: number
  precoPixBrl: number
  multiplicadorFaixa: number
  precoPelaFaixaBrl: number
  pisoBrl: number
  ajustadoPeloPiso: boolean
  versao: number
}

// ── Integrações ─────────────────────────────────────────────────────────────

export interface EventoIntegracao {
  id: string
  eventoId: string
  tipo: string
  estado: EstadoEvento
  ocorridoEm: string
  recebidoEm: string
  processadoEm: string | null
  tentativas: number
  resultado: string | null
  erro: string | null
  origem: string | null
  origemId: string | null
  clienteChave: string | null
}

export interface EventoDetalhe {
  evento: EventoIntegracao
  corpo: string
}

export type EstadoReconciliacao = 'pendente' | 'executando' | 'concluida' | 'falhou'

export interface Reconciliacao {
  id: string
  tipo: 'noturna' | 'manual' | 'carga_inicial'
  estado: EstadoReconciliacao
  de: string | null
  ate: string | null
  criadoEm: string
  iniciadoEm: string | null
  concluidoEm: string | null
  vendasLidas: number
  vendasAplicadas: number
  clientesLidos: number
  clientesCriados: number
  erro: string | null
}

export interface StatusIntegracao {
  recebeEventos: boolean
  envia: boolean
  exporta: boolean
  ultimoEventoRecebidoEm: string | null
  eventosPorEstado: Record<EstadoEvento, number>
  ultimosErros: EventoIntegracao[]
  ultimaReconciliacao: Reconciliacao | null
  versaoPrecificacao: number
  ultimoEnvioPrecificacao: EnvioPrecificacao | null
}
