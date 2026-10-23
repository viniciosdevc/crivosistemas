# AGENTS.md — como trabalhar neste repositório

Leia antes de mudar código. Vale para pessoas e agentes de IA.

O Crivo Gestão é o sistema de gestão **de uma única empresa** (single-tenant): vendas, clientes, regras de preço e usuários. Os dados de vendas e clientes nascem no crivonft; veja `docs/integracao-crivonft.md` e `docs/architecture.md`.

## Mapa
- `src/backend/Crivosistemas.Domain` — entidades e regras puras (sem EF, sem HTTP): usuários e papéis, tokens. Teste aqui primeiro.
- `src/backend/Crivosistemas.Infrastructure` — `AppDbContext`, configurações EF, migrations, e-mail, cache (Redis com prefixo `crivosistemas:`).
- `src/backend/Crivosistemas.Api` — um arquivo de endpoints por módulo em `Modules/<Módulo>/` (`Auth`, `Users`). `Common/` tem erros, paginação, auditoria, CSRF, sessão e comandos de banco.
- `src/workers/Crivosistemas.Worker` — jobs em segundo plano (hoje só limpeza de tokens; a sincronização com o crivonft entra aqui).
- `src/frontend` — Vue 3. `components/ui` é o design system (não duplique), `lib/` tem cliente HTTP, tipos (espelham os DTOs), formatação e rótulos. Páginas em `views/`, componentes de domínio em `components/<área>/`.
- `tests/` — unitários (domínio) e integração (API real + MySQL).

## Regras invioláveis
1. **O servidor decide.** Valores, permissões e papéis são validados no backend; o frontend só mostra estimativas e esconde o que o papel não pode usar.
2. **Papéis.** `admin` gerencia usuários e configurações; `operator` usa o dia a dia. Rotas de administração usam a policy `Session.AdminPolicy`. A sessão é revalidada no banco a cada requisição (desativar ou rebaixar vale na hora). O sistema nunca fica sem um admin ativo.
3. **Sem cadastro público.** O primeiro admin nasce por `--seed-admin email senha`; os demais são criados em Usuários.
4. **Dinheiro é transacional e auditável.** Quando houver, nunca edite pagamentos: estorne com motivo. Use `InTransactionAsync` (READ COMMITTED) e registre `db.Audit(...)` em mudanças administrativas e financeiras.
5. **Histórico preservado.** Usuários são desativados, não apagados; registros importados guardam o snapshot da origem.
6. **Idempotência** em tudo que o crivonft pode reenviar (chave de upsert `(origem, origem_id)`).
7. Sem segredos no repositório. Configuração por variáveis de ambiente (`.env.example`).

## Fluxo de trabalho
1. Entenda o estado em `docs/decisions.md` e `README.md`.
2. Mudou o modelo? `dotnet ef migrations add <Nome> -p src/backend/Crivosistemas.Infrastructure -s src/backend/Crivosistemas.Infrastructure -o Persistence/Migrations`. Nunca edite migrations já publicadas.
3. Antes de publicar: `dotnet build` (warnings são erros), `dotnet test` (unit + integração, precisa de MySQL), e em `src/frontend`: `npm run typecheck && npm run lint && npm test && npm run build`.
4. Commits pequenos e descritivos, em português; a `main` precisa ficar sempre funcional.

## Convenções
- C#: minimal APIs, records para DTOs com DataAnnotations (mensagens em pt-BR), erros de regra via `DomainException(code, mensagem)` → 422, conflito `ConflictException` → 409, inexistente → `NotFoundException` → 404.
- JSON camelCase, enums como string camelCase. Datas em UTC; `DateOnly` para dias de calendário.
- Frontend: `<script setup lang="ts">`, tokens de cor do Tailwind (`bg-surface`, `text-ink`, `border-line`…), nunca cores fixas, para o tema escuro funcionar. Textos em português do Brasil.
- Visual: tabelas largas com cabeçalho tingido, cards brancos de 24px de raio, botões em pílula verde-limão, modais com título grande. Sem gradientes decorativos ou sombras pesadas.
