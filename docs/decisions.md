# Decisões

Registro curto das escolhas da fase 1 (a base).

## 1. Copiar a stack do chamaezap, não o produto
Stack, infraestrutura, layout e design vêm do chamaezap (ASP.NET Core 10, EF Core + MySQL, Redis, Worker, Vue 3 + Tailwind 4 + Reka UI, nginx, Compose, CI). Foram removidos todos os módulos de loja: Storefront, Catálogos, SEO, Entrega, Agenda, WhatsApp, Cobrança e planos, Formulários, Geo, Visitas, Mídia/R2, Plataforma, a loja pública e os jobs e seeds ligados a isso.

## 2. Single-tenant
Há uma empresa só. Sem lojas, slug, `ITenantOwned`, filtro global ou trava de escrita entre contas. O `AppDbContext` ficou simples e não recebe mais um contexto de tenant.

## 3. Sem cadastro público; primeiro admin pela linha de comando
Um sistema interno não deve ter formulário de registro aberto. O primeiro admin é criado por `--seed-admin email senha [nome]` (idempotente, aplica as migrations antes). Os outros usuários são criados por admins.

## 4. Papéis `admin` e `operator`, sessão revalidada
Dois papéis bastam por ora. O papel fica no banco (`Users.Role`) e a sessão é conferida a cada requisição, para desativar ou rebaixar valer na hora. O último admin ativo é protegido. Convites por e-mail foram descartados: o admin cria a conta com uma senha inicial e a pessoa troca depois (o e-mail só é necessário para "esqueci a senha").

## 5. Usuários são desativados, não apagados
Preserva o histórico e a auditoria. Reativar é possível.

## 6. Redis compartilhável
O Redis de produção pode ser o do crivonft. Toda chave do cache e do DataProtection usa o prefixo `crivosistemas:`, e o cookie tem nome próprio (`crv_session`). O DataProtection usa `SetApplicationName("crivosistemas")`.

## 7. Uma migration inicial limpa
As migrations do chamaezap foram descartadas; `Inicial` cria só `Users`, `UserTokens` e `AuditEntries`.

## 8. Integração com o crivonft fica para a próxima etapa
O contrato (v1) está em [`integracao-crivonft.md`](integracao-crivonft.md): vendas e clientes vêm do crivonft (fonte da verdade) e as regras de preço vão daqui para lá. As telas Vendas, Clientes, Preços e regras e Integrações existem na navegação como "Em construção" para a próxima etapa encaixar o conteúdo.

## 9. Marca
"Crivo Gestão", com ícone próprio (peneira de pontos sobre o verde-limão do tema). Os tokens de cor e o tema claro/escuro do chamaezap foram mantidos.
