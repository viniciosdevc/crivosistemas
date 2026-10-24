# Contrato de integração crivonft ⇄ crivosistemas (v1.1)

**Mudanças da v1.1:** em `venda.upsert`, `cliente` pode ser `null` (venda sem comprador identificado); o crivonft repete as respostas `401`, `403`, `408` e `429` (todo o resto de `4xx` é permanente). Implementado no crivosistemas na fase 2 (backend); veja "Implementação" no fim.

Fonte da verdade das **vendas e clientes**: crivonft (PostgreSQL).
Fonte da verdade das **regras de preço** (configuração de cálculo, faixas PIX, cupons): crivosistemas (MySQL), com cópia local no crivonft.

## Autenticação (ambos os sentidos)
- `Authorization: Bearer <token>` + `X-Crivo-Signature: sha256=<hex HMAC-SHA256(secret, rawBody)>` + `X-Crivo-Timestamp: <unix segundos>`.
- O HMAC é sobre `"{timestamp}.{rawBody}"`. Rejeitar se |agora - timestamp| > 300 s.
- Env crivonft: `GESTAO_URL`, `GESTAO_TOKEN`, `GESTAO_HMAC_SECRET`, `GESTAO_SYNC_DESDE` (opcional, AAAA-MM-DD).
- Env crivosistemas: `Integrations__Crivonft__BaseUrl`, `__Token`, `__HmacSecret` (os mesmos valores), `__ExportToken` (= IMPORT_SECRET do crivonft).
- Sem URL/token configurados, o crivonft não envia nada e a venda segue normal.

## 1. Eventos crivonft → crivosistemas
`POST {GESTAO_URL}/api/integracoes/crivonft/eventos`

Corpo (um evento por request):
```json
{
  "id": "uuid do evento (outbox)",
  "tipo": "venda.upsert | venda.removida | cliente.upsert",
  "ocorrido_em": "2026-10-23T18:00:00Z",
  "dados": { ... }
}
```
Respostas: `200/201` processado; `200` com `{"duplicado":true}` se o id do evento já foi visto; `4xx` = erro permanente (crivonft marca como falho, não repete), exceto `401/403/408/429`, que repetem; `5xx`/timeout = repetir.

### `venda.upsert` → `dados`
```json
{
  "origem": "nft | cambios | site",
  "origem_id": "uuid da linha em vendas_nft / vendas_cambios / loja_pedidos",
  "pedido_id": "uuid loja_pedidos ou null",
  "data_hora": "ISO-8601 UTC",
  "status": "string original da origem",
  "status_normalizado": "pendente | pago | concluido | cancelado | reembolsado",
  "forma_pagamento": "pix | cambios | hibrido | null",
  "vendedor": "string ou null",
  "cliente": null | { "chave": "nickname ou identidade_id", "nickname": "...", "email": null, "identidade_id": null, "carteira": null, "carteira_valida": false, "carteira_loja": null, "favorito": null },
  "itens": [ { "descricao": "...", "quantidade": 1, "preco_usd": 0, "preco_brl": 0, "referencia": "listing/token id ou null" } ],
  "valores": {
    "total_brl": 0, "desconto_brl": 0, "taxa_brl": 0,
    "valor_pix_brl": 0, "valor_cambios": 0,
    "custo_brl": 0, "custo_usd": 0, "custo_cambios": 0,
    "lucro_brl": 0, "margem_percent": 0
  },
  "cupom": { "codigo": "...", "tipo": "percentual|valor_fixo|taxa_gratis" } ,
  "atualizado_em": "ISO-8601 UTC"
}
```
Chave de upsert no crivosistemas: `(origem, origem_id)`. Ignorar evento cujo `atualizado_em` seja mais antigo que o já gravado.
Valores monetários: número decimal (não string), BRL com 2 casas, USD/câmbios até 4.

### `venda.removida` → `dados`: `{ "origem": "...", "origem_id": "..." }` (venda excluída na origem: marcar como removida, não apagar).

### `cliente.upsert` → `dados`: `{ "chave": "...", "nickname": "...", "email": null, "identidade_id": null, "carteira": null, "carteira_valida": false, "carteira_loja": null, "favorito": null, "criado_em": "..." }`

**Campos de cliente `carteira`, `carteira_valida`, `carteira_loja` e `favorito` (v1.3, opcionais):** o crivonft é o dono desses dados e o Gestão os edita pela API dele (abaixo). `carteira` é o texto **como cadastrado** no crivonft (campo livre, trim, até 120 caracteres; em minúsculas só quando é um endereço 0x + 40 hexadecimais); texto que não é endereço é mantido e o painel o marca como "formato inválido" (`carteiraValida` na API, calculado). `carteira_loja` é a carteira vinculada na loja (identidade/pedido, 0x válido) quando difere de `carteira`; `favorito` é o flag do cliente (`null` = o cliente não tem cadastro lá). Vêm em `venda.upsert.cliente` e em `cliente.upsert`, e `favorito`/`carteira` também na exportação `/api/export/clientes`. O receptor **atualiza** o que vem preenchido (`favorito: false` desliga) e **nunca apaga** ao receber `null` ou ausente. Preferência da carteira no crivonft: manual (clientes_favoritos) > identidade > pedido. A reconciliação (`/reenviar`) traz também todos os clientes com favorito ou carteira cadastrados, mesmo sem venda no período.

**Editar carteira/favorito (Gestão → crivonft):** `PATCH /api/clientes/{id}` com `{ "carteira"?: string|null, "favorito"?: bool }` (admin ou operador). Com a integração configurada, o Gestão chama `PUT {BaseUrl}/api/integracoes/gestao/clientes` (mesmo Bearer + HMAC + timestamp) com `{ "nickname", "carteira"?, "favorito"? }` e só grava local a resposta `{ "ok": true, "cliente": { "nickname", "carteira", "favorito" } }`. Erro do crivonft vira `502` com `detail` claro (404 = rota ausente, publique a atualização; 401/403 = token ou segredo; 422 = a mensagem dele, ex.: cliente sem nickname; rede/tempo esgotado). Sem integração configurada grava só local. A edição é auditada (`clientes.atualizado`).

## 2. Carga inicial e reconciliação (crivosistemas puxa)
Usa as rotas já existentes do crivonft com `Authorization: Bearer <IMPORT_SECRET>`:
`GET /api/export/vendas?de=&ate=&limit=&offset=` e `GET /api/export/clientes` (formato em `crivonft/docs/API.md`).
Worker do crivosistemas roda 1x por noite para os últimos 3 dias e sob demanda pela tela Integrações.

## 3. Regras de preço crivosistemas → crivonft
`PUT {crivonft}/api/integracoes/gestao/precificacao` (mesma autenticação, com o token/segredo do crivonft)
```json
{
  "versao": 12,
  "configuracao_calculo": {
    "valor_k_feira": 13, "valor_k_trocas": 12, "moedas_por_k": 1000,
    "dolar_padrao_compra": 5.7, "dolar_padrao_venda": 6.0,
    "taxa_prata": 1, "margem_venda_cambios_percent": 20
  },
  "faixas_pix": [ { "valor_min_usd": 0, "valor_max_usd": 10, "multiplicador": 7.5, "ordem": 0, "ativo": true } ]
}
```
O crivonft aplica numa transação (substitui `loja_faixas_pix`, atualiza o singleton `loja_configuracoes_calculo`), ignora `versao` menor ou igual à última aplicada, e responde `{ "aplicada": true, "versao": 12 }`.
`GET {crivonft}/api/integracoes/gestao/precificacao` devolve o estado atual no mesmo formato (para importar na primeira vez).
Cupons ficam para a v2 do contrato.

---

## Implementação no crivosistemas

### Configuração
| Variável (`.env`) | Para quê |
| --- | --- |
| `Integrations__Crivonft__BaseUrl` | endereço do crivonft, sem barra no fim (necessário para enviar preços e puxar exportações) |
| `Integrations__Crivonft__Token` | Bearer dos dois sentidos (= `GESTAO_TOKEN`) |
| `Integrations__Crivonft__HmacSecret` | segredo do HMAC (= `GESTAO_HMAC_SECRET`) |
| `Integrations__Crivonft__ExportToken` | Bearer das rotas `/api/export/*` (= `IMPORT_SECRET`) |

Sem `Token` e `HmacSecret`, `POST /api/integracoes/crivonft/eventos` responde `503` e o worker não envia preços. Sem `BaseUrl` e `ExportToken`, a reconciliação não roda (o pedido manual responde `503`). No `docker-compose.prod.yml` as variáveis vêm do `.env` (`env_file`).

### Recepção de eventos
`POST /api/integracoes/crivonft/eventos`, sem cookie e sem o cabeçalho `X-Requested-With`. Ordem das verificações: configuração (`503`), tamanho do corpo (`400`, máximo 2 MB), Bearer + HMAC + janela de 300 s em tempo constante (`401`, com `code` `nao_autorizado` ou `timestamp_invalido`), payload.

| Resposta | Quando |
| --- | --- |
| `200 {"ok":true,"duplicado":false,"resultado":"aplicado"}` | processado (`resultado` também pode ser `ignorado: ...` quando o `atualizado_em` é mais antigo que o gravado ou a venda já foi removida) |
| `200 {"duplicado":true}` | o `id` do evento já foi processado |
| `400 {"code":"payload_invalido","erros":[...]}` | JSON ou campos inválidos (permanente) |
| `401` | credenciais ou timestamp inválidos |
| `500` | erro inesperado ao processar; o evento fica `falhou` e o crivonft deve repetir |

O corpo bruto é guardado em `EventosRecebidos` (único por `id`) antes de processar. Estados: `recebido`, `processado`, `falhou`, `rejeitado`. A tela Integrações usa os endpoints `/api/integracoes/crivonft/*` (veja `docs/architecture.md`).

### Carga inicial e reconciliação
Caminho completo (crivonft com o PR #118): `POST {BaseUrl}/api/integracoes/gestao/reenviar` com `{ "de", "ate" }` (dias em São Paulo, inclusivos, máximo 366 dias, até 5000 por origem; `truncado: true` se passar). O crivonft reenfileira vendas das três origens e clientes pela outbox dele, com payload completo, e os eventos chegam ao endpoint de eventos. A resposta é `{ ok, de, ate, nft, cambios, site, truncado }`; o crivosistemas pede janelas de 1 mês e divide ao meio quando vem `truncado`. Mesma autenticação Bearer + HMAC.

- Carga inicial: botão **Importar histórico** (Integrações, admin, com a data "desde") ou `--importar-crivonft [AAAA-MM-DD]` (por exemplo `docker compose -f docker-compose.prod.yml run --rm migrate --importar-crivonft 2024-01-01`; sem data, começa em 2020-01-01). Pode repetir: o upsert por data não duplica e uma venda gravada sem cliente ganha o vínculo.
- Noturna: uma vez por dia depois das 03:00 (São Paulo), últimos 3 dias. Sob demanda: `POST /api/integracoes/crivonft/reconciliacoes` com `{ "de", "ate" }`; a execução fica pendente até o Worker pegá-la. Em todos os casos "vendas lidas" é o número de vendas reenfileiradas no crivonft; a aplicação acontece quando os eventos chegam.
- **Fallback:** se a rota responde 404 (crivonft desatualizado), usa `GET /api/export/vendas` e `/api/export/clientes` com `ExportToken` e registra na execução o aviso `crivonft desatualizado: só vendas NFT` (câmbios e site não saem pelo export). Linhas com `pedido_id` são ignoradas (espelho de pedido do site).
- Erros ao falar com o crivonft viram `502` com `detail` claro: 404 "O crivonft ainda não tem a rota de integração; publique a atualização da loja.", 401/403 "Token ou segredo diferentes nos dois sistemas.", rede/timeout "Não foi possível falar com o crivonft...". O `503` com `code: updating` é só do nginx (API fora do ar).

### Regras de preço
Salvar em `PUT /api/precificacao` grava a nova versão, o histórico e uma linha no outbox. O Worker envia o corpo da seção 3 assinado, com backoff de 5 s dobrando até 1 h (máximo de 20 tentativas); `401/403/408/429`, `5xx` e falha de rede repetem, os demais `4xx` encerram o envio como `falhou` (use `POST /api/precificacao/reenviar` depois de corrigir). Um envio novo substitui os pendentes. `POST /api/precificacao/importar-crivonft` lê `GET /api/integracoes/gestao/precificacao` na primeira vez.

## Convites da loja (gestão → crivonft)
`GET|POST {crivonft}/api/integracoes/gestao/convites` (mesma autenticação). GET devolve `{ links, nicks }`; POST recebe `{ acao: "criar_link" | "convidar_nick" | "revogar_link" | "revogar_nick", nota?, nickname?, id? }`. `criar_link` devolve a `url` (o token só existe nessa resposta). A tela **Loja → Convites** do painel (`/api/convites`, só admin) usa essa rota; a nota leva o nome de quem convidou.

### NFTs e coleções (tela NFTs)
Rotas assinadas que o crivonft expõe em `/api/integracoes/gestao/`: `GET nfts`, `POST nfts/atualizar` e `GET|POST nfts/colecoes` (formato em `crivonft/docs/API.md`). O crivosistemas só repassa: `GET /api/nfts`, `POST /api/nfts/atualizar`, `GET|POST /api/nfts/colecoes`, todas só para admin; o que muda algo grava auditoria.

- **Preços:** o catálogo e o preço de venda continuam sendo calculados pelo crivonft, com as regras enviadas daqui. A tela mostra o preço atual, o do último dia anterior registrado e a variação, o estado do catálogo e o do envio das regras. "Atualizar preços agora" manda o crivonft buscar os valores no mercado e recalcular em segundo plano (a tela acompanha até terminar). Mudou a regra? Salvar em Preços e regras já enfileira o envio; "Enviar agora" reenvia o que ficou na fila ou falhou.
- **Coleções:** cadastradas no crivonft (`nft_colecoes`) com as posições vazias; o preenchimento dos itens continua no painel do crivonft.
- Crivonft desatualizado (sem as rotas) responde 404 e a tela mostra "publique a atualização da loja".


## Acessos por localização (mapa)
`GET /api/acessos?dias=30` (admin) repassa `GET {crivonft}/api/integracoes/gestao/acessos?dias=30` (mesma assinatura). O crivonft coleta 1 aviso por visita no navegador e guarda só agregados por dia/país/estado/cidade (sem IP) e, por cliente conectado, total de acessos, primeira/última entrada e último local. A localização vem dos cabeçalhos da Cloudflare; para ter cidade e coordenadas, ative em Cloudflare → Rules → Transform Rules → Managed Transforms → "Add visitor location headers". Sem isso só o país aparece (o centro do país serve de posição). Tela: Relatórios → Mapa de acessos.


## Vendas manuais (gestão → crivonft)
Vendas lançadas em **Vendas → Nova venda** (origem `manual`) também aparecem no crivonft, como linha de `vendas_nft`.

- **Rota:** `PUT {BaseUrl}/api/integracoes/gestao/vendas` (Bearer + HMAC). Idempotente pelo `external_id` = `dash:{id da venda}`; criar, editar, entregar e cancelar reenviam a venda inteira com o estado atual (`status`: `pendente|entregue|cancelado`). `atualizado_em` mais antigo que o gravado é ignorado.
- **Sem contabilidade em dobro:** estoque (dólar FIFO, esmeraldas), saldo de câmbios e financeiro são acertados **aqui**. O crivonft só grava a linha: não debita estoque, não lança caixa, não mexe no cofre nem no Pulse.
- **Sem loop:** o crivonft não enfileira linhas `dash:*` na outbox, nem no `/reenviar`, nem em `/api/export/vendas`; e o mapper da importação daqui também ignora `external_id` com esse prefixo.
- **Fila:** tabela `VendaEnvios` (uma linha por venda, gravada na mesma transação da venda). O Worker (`VendaEnvioJob`, a cada 5 s) envia com o mesmo backoff da precificação; 404 (crivonft ainda sem a rota) repete em vez de falhar. Admin: `GET /api/integracoes/crivonft/vendas-manuais` (contagem por estado e últimos erros) e `POST /api/integracoes/crivonft/vendas-manuais/reenviar` (recoloca todas as vendas manuais na fila; use uma vez para as que já existiam).
