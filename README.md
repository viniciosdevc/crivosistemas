# Crivo Gestão (crivosistemas)

Sistema de gestão **de uma empresa só** (single-tenant) para acompanhar vendas, clientes, regras de preço e integrações do crivonft. Fase 1 (base): login, painel e usuários. Fase 2: integração com o crivonft, vendas, clientes e precificação na API e no Worker, e as telas Vue do Painel, Vendas, Clientes, Preços e regras e Integrações.

A fonte da verdade das **vendas e clientes** é o crivonft; a das **regras de preço** é este sistema. O contrato entre os dois está em [`docs/integracao-crivonft.md`](docs/integracao-crivonft.md) e **já está implementado no backend** (veja a seção de configuração no próprio documento).

## Stack

- Backend: ASP.NET Core 10 (minimal APIs), monólito modular: `Api`, `Domain`, `Infrastructure`, `Worker`.
- Dados: EF Core 10 + MySQL 8 (migrations). Redis para cache e chaves do DataProtection (cai para memória se não houver Redis), sempre com o prefixo `crivosistemas:`.
- Frontend: Vue 3 + TypeScript estrito + Vite + Tailwind 4 + Reka UI + Pinia, tema claro e escuro.
- Infra: nginx (SPA + proxy da API), Docker Compose (dev e prod/Dokploy), GitHub Actions.

## Rodar tudo com Docker

```bash
cp .env.example .env        # defina MYSQL_PASSWORD e MYSQL_ROOT_PASSWORD
docker compose up -d --build
# primeiro administrador (não existe cadastro público):
docker compose run --rm migrate --seed-admin voce@empresa.com 'senha-forte-123' 'Seu Nome'
```

Abra http://localhost:8080 e entre com o e-mail e a senha acima. O comando `--seed-admin` é idempotente: se o e-mail já existir, não altera nada. Cuidado com o histórico do shell, que guarda a senha.

## Desenvolvimento local

Precisa de .NET SDK 10, Node 22, MySQL 8 e (opcional) Redis.

```bash
# banco: usuário, senha e banco "crivosistemas" em localhost:3306 (veja appsettings.Development.json)
dotnet run --project src/backend/Crivosistemas.Api -- --seed-admin voce@empresa.com 'senha-forte-123'
dotnet run --project src/backend/Crivosistemas.Api          # http://localhost:5080 (migra o banco ao subir em Development)
cd src/frontend && npm ci && npm run dev                    # http://localhost:5173 (proxy de /api para a API)
```

## Verificação

```bash
dotnet build Crivosistemas.slnx          # warnings são erros
dotnet test Crivosistemas.slnx           # integração usa MySQL local (CRIVOSISTEMAS_TEST_DB_SERVER; padrão crivosistemas/crivosistemas@localhost)
cd src/frontend && npm run typecheck && npm run lint && npm test && npm run build
```

## Produção (Dokploy)

`docker-compose.prod.yml` sobe `migrate`, `api`, `worker` e `web`, com MySQL e Redis **externos** (variáveis `ConnectionStrings__Database` e `ConnectionStrings__Redis` no `.env`). O Redis pode ser o mesmo do crivonft porque toda chave usa o prefixo `crivosistemas:`. Aponte o domínio para o serviço `web`, porta 80, e defina `Auth__RequireHttps=true`.

## Documentação

- [`docs/architecture.md`](docs/architecture.md): visão geral, módulos, autenticação e papéis.
- [`docs/decisions.md`](docs/decisions.md): decisões e o que ficou de fora.
- [`docs/integracao-crivonft.md`](docs/integracao-crivonft.md): contrato de integração com o crivonft, configuração e operação.
- [`AGENTS.md`](AGENTS.md): regras para trabalhar no código.
