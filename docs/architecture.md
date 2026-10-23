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

## Endpoints atuais

| Rota | Quem | O quê |
| --- | --- | --- |
| `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` | qualquer / logado | sessão |
| `POST /api/auth/change-password` | logado | troca de senha |
| `POST /api/auth/forgot-password`, `POST /api/auth/reset-password` | anônimo | redefinição por e-mail |
| `GET/POST /api/users`, `PUT /api/users/{id}`, `POST /api/users/{id}/deactivate`, `POST /api/users/{id}/activate` | admin | usuários |
| `GET /health/live`, `GET /health/ready` | anônimo | health checks (ready confere MySQL e Redis) |

Documentação interativa em `/docs` (Scalar) e `/openapi/v1.json`.

## Dados

Tabelas: `Users`, `UserTokens`, `AuditEntries`. Timestamps em UTC. Ids são Guid v7 gerados no código. Uma migration inicial limpa (`Inicial`).

## Frontend

`src/frontend`: `layouts/AdminLayout.vue` (menu lateral recolhível, trilha por `meta.section`/`meta.title`, tema claro/escuro, menu da conta), `components/ui/*` (design system), `styles/main.css` (tokens). Rotas em `router/index.ts`; o painel fica em `/painel`: Painel, Vendas, Clientes, Preços e regras, Integrações e Usuários (só admin). O cliente HTTP (`lib/api.ts`) é same-origin com cookie e normaliza erros em `ApiError`.

## Worker

Hoje roda só a limpeza de tokens de senha vencidos. É o lugar da sincronização noturna e sob demanda com o crivonft (carga inicial e reconciliação dos últimos 3 dias).

## Integração com o crivonft

Segue o contrato em [`integracao-crivonft.md`](integracao-crivonft.md): eventos HTTP assinados com HMAC do crivonft para cá, carga e reconciliação por pull, e regras de preço daqui para lá. **Nada disso está implementado nesta fase.** O prefixo `crivosistemas:` no Redis existe justamente para dividir a instância com o crivonft.
