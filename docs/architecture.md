# Arquitetura

## Visão geral

```
navegador ──► web (nginx: SPA + proxy /api) ──► api (ASP.NET Core) ──► MySQL
                                                   │  └──────────────► Redis (cache + chaves de sessão, prefixo crivosistemas:)
                                  worker (.NET) ───┘
```

Monólito modular: uma API com módulos por pasta (`Auth`, `Users`) sobre um domínio puro e uma camada de infraestrutura. Um worker separado roda tarefas em segundo plano. Não há microsserviços.

O sistema é **single-tenant**: existe uma empresa só, então não há lojas, slug, filtro por tenant nem vínculo usuário-loja. Todo usuário vê os mesmos dados; o que muda é o papel.

## Autenticação, papéis e proteção

- Cookie de sessão `crv_session` (`HttpOnly`, `SameSite=Lax`, `Secure` em produção, expiração deslizante de 14 dias). As chaves do DataProtection ficam no Redis (`crivosistemas:dataprotection-keys`), então as sessões sobrevivem a deploys e funcionam com várias instâncias.
- **Sem cadastro público.** O primeiro administrador é criado pela linha de comando: `--seed-admin <email> <senha> [nome]` (aplica as migrations antes e é idempotente). Os demais são criados por um admin na tela Usuários.
- Papéis: `admin` (usuários e configurações) e `operator` (dia a dia). As rotas `/api/users` exigem a policy de admin; o frontend também esconde o item Usuários dos operadores, mas a regra de verdade está no servidor.
- A cada requisição o cookie é revalidado no banco (`Session.ValidateAsync`): usuário desativado perde o acesso na hora e mudança de papel vale imediatamente.
- O sistema nunca fica sem um administrador ativo: o último admin não pode ser desativado nem rebaixado, e ninguém desativa a si mesmo.
- Senhas com `PasswordHasher` (PBKDF2). O login responde a mesma mensagem para e-mail inexistente, senha errada e conta desativada. A troca de senha exige a senha atual; "esqueci a senha" envia um link de uso único (hash SHA-256 no banco, validade de 1 hora) e sempre responde 204.
- CSRF: requisições que alteram estado em `/api` exigem o cabeçalho `X-Requested-With`; o CORS é restrito.
- Rate limiting por IP nas rotas de autenticação (10 por minuto).
- Erros: o handler global devolve `ProblemDetails` com `code` estável; validações 400 com erros por campo; regras de negócio 422; conflitos 409; logs estruturados (Serilog).
- Auditoria: `AuditEntry` registra mudanças administrativas (usuário criado, desativado, papel alterado, senha trocada) na mesma transação da mudança.

## Endpoints

Papéis: **admin** (só administradores), **logado** (admin ou operador), **HMAC** (máquina a máquina, sem cookie nem CSRF).

| Método e rota | Quem | O quê |
| --- | --- | --- |
| `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` | qualquer / logado | sessão |
| `POST /api/auth/change-password` | logado | troca de senha |
| `POST /api/auth/forgot-password`, `POST /api/auth/reset-password` | anônimo | redefinição por e-mail |
| `GET/POST /api/users`, `PUT /api/users/{id}`, `POST /api/users/{id}/deactivate`, `POST /api/users/{id}/activate` | admin | usuários |
| `POST /api/integracoes/crivonft/eventos` | HMAC | recebe eventos do crivonft (503 sem configuração) |
| `GET /api/integracoes/crivonft/status` | admin | último evento, contagens por estado, últimos erros, última reconciliação, último envio de preços |
| `GET /api/integracoes/crivonft/eventos` | admin | eventos recebidos; filtros `estado`, `tipo`, `busca`, `page`, `pageSize` |
| `GET /api/integracoes/crivonft/eventos/{id}` | admin | evento com o corpo bruto |
| `POST /api/integracoes/crivonft/eventos/{id}/reprocessar` | admin | reaplica um evento gravado |
| `GET /api/integracoes/crivonft/reconciliacoes` | admin | execuções de reconciliação |
| `POST /api/integracoes/crivonft/reconciliacoes` | admin | pede reconciliação de `{de, ate}` (o Worker executa; 202) |
| `GET /api/vendas` | logado | lista paginada; filtros `de`, `ate`, `origem`, `statusNormalizado`, `formaPagamento`, `clienteId`, `busca`, `incluirRemovidas`, `ordem` (`data`, `total`, `lucro`), `dir` |
| `GET /api/vendas/{id}` | logado | itens, valores, cupom e histórico de eventos |
| `GET /api/vendas/resumo` | logado | faturamento, lucro, ticket médio, quantidade, por origem e por dia (`de`, `ate`, `origem`) |
| `GET /api/clientes` | logado | busca, total gasto, nº de compras, última compra; `ordem` (`nome`, `totalGasto`, `compras`, `ultimaCompra`) |
| `GET /api/clientes/{id}` | logado | detalhe com histórico de vendas |
| `GET /api/precificacao` | admin | configuração de cálculo, faixas PIX, versão, último envio |
| `PUT /api/precificacao` | admin | salva `{versao, configuracao, faixasPix}`; 409 se a versão mudou; 422 se inválido |
| `GET /api/precificacao/historico`, `GET /api/precificacao/envios` | admin | histórico (quem, quando, antes/depois) e outbox de envios |
| `POST /api/precificacao/reenviar` | admin | recoloca na fila o último envio não concluído |
| `POST /api/precificacao/importar-crivonft` | admin | traz as regras atuais do crivonft (primeira vez; `forcar` depois) |
| `GET /api/precificacao/calculo?usd=` | admin | preço PIX final em BRL pelas regras salvas |
| `GET /health/live`, `GET /health/ready` | anônimo | health checks (ready confere MySQL e Redis) |

Documentação interativa em `/docs` (Scalar) e `/openapi/v1.json`. Datas de período (`de`, `ate`) são dias `AAAA-MM-DD` em America/Sao_Paulo; os instantes saem em UTC (ISO-8601) e os campos `dia` já vêm no fuso de São Paulo.

## Dados

Tabelas: `Users`, `UserTokens`, `AuditEntries` (migration `Inicial`) e, na migration `IntegracaoVendasPrecificacao`: `Clientes` (chave única), `Vendas` (única por `Origem + OrigemId`), `VendaItens`, `EventosRecebidos` (id do evento único), `Reconciliacoes`, `PrecificacaoConfigs` (singleton com `Versao`), `FaixasPix`, `PrecificacaoHistoricos` e `PrecificacaoEnvios` (outbox). Timestamps em UTC. Ids são Guid v7 gerados no código.

Regras: faturamento conta só vendas `pago` ou `concluido` e não removidas (pendente, cancelada, reembolsada e removida ficam de fora). Uma venda removida na origem é marcada (`RemovidaEm`), nunca apagada. Evento mais antigo que o `atualizado_em` gravado é ignorado.

## Frontend

`src/frontend`: `layouts/AdminLayout.vue` (menu lateral recolhível, trilha por `meta.section`/`meta.title`, tema claro/escuro, menu da conta), `components/ui/*` (design system), `styles/main.css` (tokens). Rotas em `router/index.ts`; o painel fica em `/painel`: Painel, Vendas (`/vendas/:id`), Clientes (`/clientes/:id`), Preços e regras, Integrações e Usuários; as três últimas são só do admin (rota e menu). Filtros das listas ficam na URL (`lib/urlFilters.ts`) e as datas de período seguem America/Sao_Paulo (`lib/period.ts`). O cliente HTTP (`lib/api.ts`) é same-origin com cookie e normaliza erros em `ApiError`.

## Worker

- `TokenCleanupJob`: limpa tokens de senha vencidos.
- `ReconciliacaoJob`: a cada 30 s agenda a rodada noturna (uma por dia, depois das 03:00 de São Paulo, últimos 3 dias) e executa as reconciliações pendentes pedidas pela tela Integrações. Puxa `GET /api/export/clientes` e `/api/export/vendas` com Bearer `ExportToken` e aplica com a mesma regra de upsert dos eventos.
- `PrecificacaoEnvioJob`: a cada 5 s entrega o outbox de preços (`PUT /api/integracoes/gestao/precificacao`, HMAC), com backoff de 5 s dobrando até 1 h, até 20 tentativas. 4xx permanente (menos 401/403/408/429) desiste.
- Sem `Integrations:Crivonft` configurado, os dois últimos não fazem nada.

Carga inicial completa: `--importar-crivonft [AAAA-MM-DD]` no executável da API (`docker compose run --rm migrate --importar-crivonft 2024-01-01`).

## Integração com o crivonft

Segue o contrato em [`integracao-crivonft.md`](integracao-crivonft.md): eventos HTTP assinados com HMAC do crivonft para cá (`IngestaoService`), carga e reconciliação por pull (`ReconciliacaoService`), e regras de preço daqui para lá (`PrecificacaoService` grava, `PrecificacaoEnvioService` entrega). O cliente HTTP é `ICrivonftClient` (HttpClient tipado, trocado por um falso nos testes). O cálculo do preço PIX é a função pura `CalculoPreco.PrecoPix` no Domain, réplica de `calculateFromUsd` do crivonft. O prefixo `crivosistemas:` no Redis existe para dividir a instância com o crivonft.
