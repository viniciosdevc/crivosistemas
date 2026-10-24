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

export type Origem = 'nft' | 'cambios' | 'site' | 'manual'
export type StatusVenda = 'pendente' | 'pago' | 'concluido' | 'cancelado' | 'reembolsado'
export type FormaPagamento = 'pix' | 'cambios' | 'hibrido'

export interface ClienteResumo {
  id: string
  nickname: string
  email: string | null
  /** Endereço 0x… (minúsculas) ou null. */
  carteira: string | null
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
  /** Só em vendas de origem 'manual'. */
  manual: VendaManual | null
}

export type OrigemCusto = 'estoque_usd' | 'cambios' | 'estoque_esmeralda' | 'manual'

export interface MovimentoVenda {
  tipo: TipoEstoque
  quantidade: number
  custoUnitario: number
  motivo: MotivoMovimento
  criadoEm: string
}

export interface VendaManual {
  origemCusto: OrigemCusto | null
  quantidadeEsmeraldas: number | null
  usaTaxaPrata: boolean
  dolarCompraUsado: number | null
  observacao: string | null
  movimentos: MovimentoVenda[]
}

export interface NovaVenda {
  dataHora: string
  nicknameComprador: string
  nomeItem: string
  formaPagamento: FormaPagamento
  origemCusto: OrigemCusto
  custoUsd: number
  custoCambios: number
  quantidadeEsmeraldas: number
  usaTaxaPrata: boolean
  valorVendaPix: number
  valorVendaCambios: number
  observacao: string
  entregue: boolean
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
  carteira: string | null
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
  /** Observação que não é falha, ex.: execução parcial por crivonft desatualizado. */
  aviso: string | null
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

/** Regras de preço vigentes, legíveis por qualquer usuário logado (calculadora e venda manual). */
export interface RegrasPrecificacao {
  versao: number
  configuracao: ConfigCalculo
  faixasPix: FaixaPix[]
}

// ── Estoque ─────────────────────────────────────────────────────────────────

export type TipoEstoque = 'usd' | 'esmeraldas'
export type OrigemLote = 'compra' | 'ajuste' | 'importacao_crivonft'
export type MotivoMovimento = 'compra' | 'venda' | 'estorno_venda' | 'ajuste' | 'cancelamento' | 'importacao' | 'edicao'

/** USD: custoMedio em R$ por US$. Esmeraldas: custoMedio em US$ por esmeralda. */
export interface ResumoEstoque {
  tipo: TipoEstoque
  saldo: number
  custoMedio: number
  custoMedioCalculado: number
  custoMedioFixo: number | null
  valorEmEstoque: number
  lotes: number
}

export interface ResumoEstoques {
  usd: ResumoEstoque
  esmeraldas: ResumoEstoque
}

export interface LoteEstoque {
  id: string
  tipo: TipoEstoque
  origem: OrigemLote
  /** AAAA-MM-DD */
  dataCompra: string
  quantidade: number
  saldo: number
  custoTotal: number
  custoUnitario: number
  observacao: string | null
  criadoEm: string
  canceladoEm: string | null
  motivoCancelamento: string | null
  editavel: boolean
}

export interface MovimentoEstoque {
  id: string
  tipo: TipoEstoque
  loteId: string
  /** Positivo entra, negativo sai. */
  quantidade: number
  custoUnitario: number
  motivo: MotivoMovimento
  vendaId: string | null
  estornado: boolean
  observacao: string | null
  usuario: string | null
  criadoEm: string
}

export interface CompraEstoque {
  data: string
  quantidade: number
  custoTotal: number
  observacao: string
}

export interface AjusteEstoque {
  direcao: 'entrada' | 'saida'
  quantidade: number
  custoUnitario: number | null
  motivo: string
}

export interface ResultadoImportacaoEstoque {
  usdImportados: number
  esmeraldasImportados: number
  ignorados: number
}

// ── Câmbios ─────────────────────────────────────────────────────────────────

export type TipoMovimentoCambios = 'venda' | 'venda_manual' | 'ajuste' | 'estorno'

export interface ResumoCambios {
  saldo: number
  totalVendidoQuantidade: number
  totalVendidoBrl: number
}

export interface MovimentoCambios {
  id: string
  tipo: TipoMovimentoCambios
  /** Positivo entra, negativo sai. */
  quantidade: number
  saldoAnterior: number
  saldoNovo: number
  valorBrl: number | null
  clienteId: string | null
  nickname: string | null
  vendaId: string | null
  estornado: boolean
  observacao: string | null
  usuario: string | null
  dataHora: string
}

// ── Financeiro ──────────────────────────────────────────────────────────────
export type TipoLancamento = 'entrada' | 'saida'
export type StatusLancamento = 'pendente' | 'confirmado' | 'cancelado'

export interface Lancamento {
  id: string
  tipo: TipoLancamento
  status: StatusLancamento
  origem: 'manual' | 'venda' | 'venda_cambios' | 'compra_usd'
  categoria: string
  descricao: string | null
  valorBrl: number
  data: string
  vendaId: string | null
  loteEstoqueId: string | null
  movimentoCambiosId: string | null
  motivoCancelamento: string | null
  criadoPorNome: string | null
  criadoEm: string
  confirmadoEm: string | null
  canceladoEm: string | null
  automatico: boolean
}

export interface TotaisFinanceiro { entradas: number; saidas: number; resultado: number }
export interface CategoriaTotal { categoria: string; tipo: TipoLancamento; total: number; quantidade: number }
export interface ResumoFinanceiro {
  saldo: number
  hoje: TotaisFinanceiro
  mes: TotaisFinanceiro
  de: string
  ate: string
  periodo: TotaisFinanceiro
  pendenteReceber: number
  pendentePagar: number
  pendentes: number
  porCategoria: CategoriaTotal[]
}
export interface NovoLancamento {
  tipo: TipoLancamento
  categoria: string
  descricao: string
  valorBrl: number
  data: string
  pendente: boolean
}
