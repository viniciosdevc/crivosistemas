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

## 8. Integração com o crivonft
O contrato está em [`integracao-crivonft.md`](integracao-crivonft.md): vendas e clientes vêm do crivonft (fonte da verdade) e as regras de preço vão daqui para lá. Implementado na fase 2 (backend); as telas Vue entram em outra etapa.

- **Evento bruto primeiro.** O corpo é gravado em `EventosRecebidos` (id único) antes de processar. Repetição responde `{"duplicado":true}`; payload inválido é 400 e fica "rejeitado"; erro inesperado é 500 e fica "falhou" (o crivonft repete e o admin pode reprocessar).
- **Upsert por data.** Chave `(origem, origem_id)`; evento com `atualizado_em` menor que o gravado é ignorado. `venda.removida` marca a venda; se chegar antes do upsert, cria um marcador para que um upsert atrasado não a reviva.
- **Cliente opcional.** `venda.upsert` pode vir com `cliente: null`; a venda fica sem cliente.
- **Reconciliação por fila.** O pedido manual vira uma linha "pendente" que o Worker executa, porque a API não faz chamadas longas ao crivonft.
- **Limitação do export.** As linhas de `/api/export/vendas` não trazem os itens do site, nem desconto/taxa/cupom, e não trazem `pedido_id` quando o crivonft não o expõe. A reconciliação ignora linhas com `pedido_id` (espelho de pedido do site, que já chega por evento) para não contar a venda duas vezes. Se o crivonft passar a omitir `pedido_id` no export, as vendas do site podem ser contadas em dobro: pedir ao crivonft que o inclua ou que filtre `vendas_nft` espelhadas.
- **Preços: este sistema manda.** Salvar sobe `Versao`, grava histórico e auditoria e escreve no outbox na mesma transação; um envio novo substitui os pendentes. O PUT otimista usa a versão carregada (409 se mudou). Ao importar do crivonft a versão local nunca regride.
- **Cálculo em `double`.** `CalculoPreco` usa ponto flutuante e `Math.round` do JS (metade sobe) para bater centavo a centavo com o crivonft; a persistência usa `decimal`.

## 9. Marca
"Crivo Gestão", com ícone próprio (peneira de pontos sobre o verde-limão do tema). Os tokens de cor e o tema claro/escuro do chamaezap foram mantidos.
