using System.Text.Json;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Integracoes;
using Crivosistemas.Api.Modules.Vendas;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Api.Modules.Clientes;

/// <param name="Carteira">Texto como cadastrado no crivonft (livre, até 120); <paramref name="CarteiraValida"/> diz se é um endereço 0x + 40 hex.</param>
/// <param name="CarteiraLoja">Carteira vinculada na loja, quando difere de <paramref name="Carteira"/>.</param>
public sealed record ClienteListaDto(Guid Id, string Nickname, string? Email, string? Carteira, bool CarteiraValida, string? CarteiraLoja, bool Favorito, decimal TotalGasto, int Compras, DateTime? UltimaCompra);
public sealed record ClienteDetalheDto(ClienteListaDto Cliente, string Chave, string? IdentidadeId, DateTime? CriadoEmOrigem, IReadOnlyList<VendaListaDto> Vendas);

/// <summary>Clientes vindos do crivonft. Total gasto, nº de compras e última compra contam só vendas faturáveis (pagas ou concluídas, não removidas).</summary>
public static class ClientesEndpoints
{
    public static void MapClientesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/clientes").WithTags("Clientes").RequireAuthorization();
        g.MapGet("", Listar).WithSummary("Lista paginada com busca (nickname, e-mail, carteira) e ordenação por nome, totalGasto, compras ou ultimaCompra.");
        g.MapGet("/crm", Crm).WithSummary("Análise de CRM: segmentos, ticket médio, frequência, LTV, concentração de receita e lista de clientes para reativar.");
        g.MapGet("/{id:guid}", Detalhe).WithSummary("Detalhe do cliente com o histórico de vendas.");
        g.MapPatch("/{id:guid}", Atualizar).WithSummary("Edita a carteira (texto livre até 120; vazio remove) e/ou o favorito. Com a integração configurada, o crivonft (dono do dado) é atualizado primeiro.");
    }

    private static async Task<IResult> Listar(AppDbContext db, string? busca, string? ordem, string? dir, bool? favorito, string? carteira, string? ordenar, int? page, int? pageSize, CancellationToken ct)
    {
        var q = db.Clientes.AsNoTracking().AsQueryable();
        if (favorito == true) q = q.Where(c => c.Favorito);
        q = (carteira?.ToLowerInvariant()) switch
        {
            "com" => q.Where(c => c.Carteira != null && c.Carteira != ""),
            "sem" => q.Where(c => c.Carteira == null || c.Carteira == ""),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim();
            q = q.Where(c => c.Nickname.Contains(b) || c.Chave.Contains(b) || (c.Carteira != null && c.Carteira.Contains(b)) || (c.Email != null && c.Email.Contains(b)));
        }
        var proj = q.Select(c => new ClienteLinha
        {
            Id = c.Id, Nickname = c.Nickname, Email = c.Email, Carteira = c.Carteira, CarteiraLoja = c.CarteiraLoja, Favorito = c.Favorito,
            Total = db.Vendas.Where(v => v.ClienteId == c.Id).Where(VendaConsultas.Faturavel).Sum(v => (decimal?)v.TotalBrl) ?? 0,
            Compras = db.Vendas.Where(v => v.ClienteId == c.Id).Where(VendaConsultas.Faturavel).Count(),
            Ultima = db.Vendas.Where(v => v.ClienteId == c.Id).Where(VendaConsultas.Faturavel).Max(v => (DateTime?)v.DataHora),
        });
        var desc = Paging.Descending(dir);
        // "ordenar=favoritos": favoritos primeiro, dentro de cada grupo vale a ordenação pedida.
        var favPrimeiro = string.Equals(ordenar, "favoritos", StringComparison.OrdinalIgnoreCase);
        var chave = (ordem?.ToLowerInvariant()) switch { "totalgasto" or "compras" or "ultimacompra" => ordem!.ToLowerInvariant(), _ => "nome" };
        IOrderedQueryable<ClienteLinha> final;
        if (favPrimeiro)
        {
            var f = proj.OrderByDescending(x => x.Favorito);
            final = chave switch
            {
                "totalgasto" => desc ? f.ThenByDescending(x => x.Total) : f.ThenBy(x => x.Total),
                "compras" => desc ? f.ThenByDescending(x => x.Compras) : f.ThenBy(x => x.Compras),
                "ultimacompra" => desc ? f.ThenByDescending(x => x.Ultima) : f.ThenBy(x => x.Ultima),
                _ => f.ThenBy(x => x.Nickname),
            };
        }
        else
        {
            final = chave switch
            {
                "totalgasto" => desc ? proj.OrderByDescending(x => x.Total) : proj.OrderBy(x => x.Total),
                "compras" => desc ? proj.OrderByDescending(x => x.Compras) : proj.OrderBy(x => x.Compras),
                "ultimacompra" => desc ? proj.OrderByDescending(x => x.Ultima) : proj.OrderBy(x => x.Ultima),
                _ => proj.OrderBy(x => x.Nickname),
            };
        }
        var r = await final.ThenBy(x => x.Id).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<ClienteListaDto>(r.Items.Select(x => Lista(x.Id, x.Nickname, x.Email, x.Carteira, x.CarteiraLoja, x.Favorito, x.Total, x.Compras, x.Ultima)).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Crm(AppDbContext db, int? inativoDias, CancellationToken ct)
    {
        var dias = Math.Clamp(inativoDias ?? 60, 7, 365);
        var compras = await db.Vendas.AsNoTracking().Where(VendaConsultas.Faturavel).Where(v => v.ClienteId != null)
            .GroupBy(v => v.ClienteId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(v => v.TotalBrl), N = g.Count(), Primeira = g.Min(v => v.DataHora), Ultima = g.Max(v => v.DataHora) })
            .ToDictionaryAsync(x => x.Id, ct);
        var clientes = await db.Clientes.AsNoTracking().Select(c => new { c.Id, c.Nickname, c.Favorito }).ToListAsync(ct);
        var entrada = clientes.Select(c => compras.TryGetValue(c.Id, out var x)
            ? new ClienteCompras(c.Id, c.Nickname, c.Favorito, x.Total, x.N, x.Primeira, x.Ultima)
            : new ClienteCompras(c.Id, c.Nickname, c.Favorito, 0, 0, null, null)).ToList();
        return Results.Ok(new { inativoDias = dias, analise = CrmClientes.Analisar(entrada, DateTime.UtcNow, dias) });
    }

    private static async Task<IResult> Detalhe(Guid id, AppDbContext db, CancellationToken ct)
    {
        var c = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Cliente não encontrado.");
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Cliente).Include(v => v.Itens).AsSplitQuery().Where(v => v.ClienteId == id && v.RemovidaEm == null)
            .OrderByDescending(v => v.DataHora).Take(500).ToListAsync(ct);
        var fat = vendas.Where(v => v.Faturavel).ToList();
        var resumo = Lista(c.Id, c.Nickname, c.Email, c.Carteira, c.CarteiraLoja, c.Favorito, fat.Sum(v => v.TotalBrl), fat.Count, fat.Count == 0 ? null : fat.Max(v => v.DataHora));
        return Results.Ok(new ClienteDetalheDto(resumo, c.Chave, c.IdentidadeId, c.CriadoEmOrigem, vendas.Select(VendasEndpoints.ToLista).ToList()));
    }

    private sealed class ClienteLinha
    {
        public Guid Id { get; set; }
        public string Nickname { get; set; } = "";
        public string? Email { get; set; }
        public string? Carteira { get; set; }
        public string? CarteiraLoja { get; set; }
        public bool Favorito { get; set; }
        public decimal Total { get; set; }
        public int Compras { get; set; }
        public DateTime? Ultima { get; set; }
    }

    private static ClienteListaDto Lista(Guid id, string nickname, string? email, string? carteira, string? carteiraLoja, bool favorito, decimal total, int compras, DateTime? ultima) =>
        new(id, nickname, email, carteira, PayloadParser.CarteiraValida(carteira), carteiraLoja, favorito, total, compras, ultima);

    /// <summary>
    /// Edita carteira e/ou favorito. O crivonft é o dono desses dados: com a integração configurada ele é chamado primeiro
    /// (PUT /api/integracoes/gestao/clientes) e só a resposta dele é gravada aqui; erro dele vira 502 sem gravar nada.
    /// </summary>
    private static async Task<IResult> Atualizar(Guid id, [FromBody] JsonElement corpo, AppDbContext db, IOptions<CrivonftOptions> o, ICrivonftClient crivonft, HttpContext http, CancellationToken ct)
    {
        var c = await db.Clientes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Cliente não encontrado.");
        if (corpo.ValueKind != JsonValueKind.Object) throw new DomainException("corpo_invalido", "Envie um objeto JSON com carteira e/ou favorito.");

        var temCarteira = corpo.TryGetProperty("carteira", out var vc);
        string? carteira = null;
        if (temCarteira && vc.ValueKind != JsonValueKind.Null)
        {
            if (vc.ValueKind != JsonValueKind.String) throw new DomainException("carteira_invalida", "A carteira deve ser um texto.");
            var t = vc.GetString()!.Trim();
            if (t.Length > PayloadParser.CarteiraMax) throw new DomainException("carteira_longa", $"A carteira aceita até {PayloadParser.CarteiraMax} caracteres.");
            carteira = PayloadParser.TextoCarteira(t); // parece 0x mas é inválido? aceita e só fica marcada como inválida
        }
        bool? favorito = null;
        if (corpo.TryGetProperty("favorito", out var vf) && vf.ValueKind != JsonValueKind.Null)
        {
            if (vf.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new DomainException("favorito_invalido", "O favorito deve ser verdadeiro ou falso.");
            favorito = vf.GetBoolean();
        }
        if (!temCarteira && favorito is null) throw new DomainException("nada_a_alterar", "Informe a carteira e/ou o favorito.");

        var antes = new { c.Carteira, c.Favorito };
        string? carteiraFinal = temCarteira ? carteira : c.Carteira;
        bool favoritoFinal = favorito ?? c.Favorito;

        if (o.Value.Envia)
        {
            var enviar = new Dictionary<string, object?> { ["nickname"] = c.Nickname };
            if (temCarteira) enviar["carteira"] = carteira;
            if (favorito is { } f) enviar["favorito"] = f;
            try
            {
                var r = await crivonft.ChamarGestaoAsync(HttpMethod.Put, "/api/integracoes/gestao/clientes", enviar, ct);
                if (!r.Sucesso) return RecusaDoCrivonft(r);
                // A resposta do crivonft é a verdade: grava o que ele gravou.
                try
                {
                    using var doc = JsonDocument.Parse(r.Corpo);
                    if (doc.RootElement.TryGetProperty("cliente", out var cli) && cli.ValueKind == JsonValueKind.Object)
                    {
                        if (temCarteira) carteiraFinal = cli.TryGetProperty("carteira", out var rc) && rc.ValueKind == JsonValueKind.String ? PayloadParser.TextoCarteira(rc.GetString()) : null;
                        if (favorito is not null && cli.TryGetProperty("favorito", out var rf) && rf.ValueKind is JsonValueKind.True or JsonValueKind.False) favoritoFinal = rf.GetBoolean();
                    }
                }
                catch (JsonException) { /* corpo fora do padrão: vale o que foi enviado */ }
            }
            catch (CrivonftException e)
            {
                return IntegracoesEndpoints.Crivonft502(e);
            }
        }

        c.Carteira = carteiraFinal;
        c.Favorito = favoritoFinal;
        // O vínculo da loja só é mostrado quando difere da carteira em uso.
        if (c.CarteiraLoja is not null && c.CarteiraLoja == c.Carteira) c.CarteiraLoja = null;
        c.AtualizadoEm = DateTime.UtcNow;
        db.Audit(http, "clientes.atualizado", "Cliente", c.Id, new { c.Nickname, antes, depois = new { c.Carteira, c.Favorito }, viaCrivonft = o.Value.Envia });
        await db.SaveChangesAsync(ct);

        var fat = db.Vendas.AsNoTracking().Where(v => v.ClienteId == id).Where(VendaConsultas.Faturavel);
        var total = await fat.SumAsync(v => (decimal?)v.TotalBrl, ct) ?? 0;
        var compras = await fat.CountAsync(ct);
        var ultima = await fat.MaxAsync(v => (DateTime?)v.DataHora, ct);
        return Results.Ok(Lista(c.Id, c.Nickname, c.Email, c.Carteira, c.CarteiraLoja, c.Favorito, total, compras, ultima));
    }

    /// <summary>422 do crivonft traz a mensagem pensada para quem usa a tela; o painel a repassa no detalhe de um 502.</summary>
    private static IResult RecusaDoCrivonft(GestaoResposta r)
    {
        if (r.StatusHttp is 400 or 409 or 422)
        {
            var msg = "O crivonft recusou a alteração.";
            try { if (JsonDocument.Parse(r.Corpo).RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) msg = e.GetString() ?? msg; }
            catch (JsonException) { /* corpo fora do padrão: mensagem genérica */ }
            return Results.Json(new { title = msg, detail = msg, code = "crivonft_recusou", status = 502, statusCrivonft = r.StatusHttp }, statusCode: StatusCodes.Status502BadGateway);
        }
        return IntegracoesEndpoints.Crivonft502(new CrivonftException($"HTTP {r.StatusHttp}: {(r.Corpo.Length > 200 ? r.Corpo[..200] : r.Corpo)}", r.StatusHttp));
    }
}
