using Crivosistemas.Domain.Identity;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Common;

public static class DatabaseTasks
{
    public static async Task MigrateAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");
        // O contêiner do banco pode ainda estar subindo (ou reiniciando) quando o deploy começa: espera em vez de falhar.
        // CanConnect é falso quando o servidor está no ar mas o banco ainda não existe; por isso migra direto: o EF cria o banco
        // se faltar e só lança exceção enquanto o servidor estiver inacessível.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                logger.LogInformation("Aplicando {Count} migration(s) pendente(s): {Names}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync();
                break;
            }
            catch (Exception e) when (attempt < 40)
            {
                logger.LogWarning("Banco ainda inacessível ({Message}); nova tentativa ({Attempt}/40)", e.Message, attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    /// <summary>Comando de operação: <c>--seed-admin email senha [nome]</c> cria o primeiro administrador. Não há cadastro público.
    /// Não faz nada se o e-mail já existir, então pode rodar a cada deploy sem efeito colateral.</summary>
    public static async Task<int> SeedAdminAsync(IServiceProvider services, string email, string password, string? name)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var normalized = User.NormalizeEmail(email);
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(normalized))
        {
            Console.Error.WriteLine($"E-mail inválido: {email}");
            return 1;
        }
        if (password.Length < 8)
        {
            Console.Error.WriteLine("A senha deve ter pelo menos 8 caracteres.");
            return 1;
        }
        if (await db.Users.AnyAsync(u => u.Email == normalized))
        {
            Console.WriteLine($"Já existe um usuário com o e-mail {normalized}; nada foi alterado.");
            return 0;
        }

        var user = new User { Name = string.IsNullOrWhiteSpace(name) ? "Administrador" : name.Trim(), Email = normalized, Role = UserRole.Admin };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        db.Audit(null, "users.seeded_admin", "User", user.Id, new { email = normalized });
        await db.SaveChangesAsync();
        Console.WriteLine($"Administrador {normalized} criado.");
        return 0;
    }

    /// <summary>Comando de operação: <c>--importar-crivonft [AAAA-MM-DD]</c> faz a carga completa inicial (vendas desde a data, e todos os clientes)
    /// puxando <c>/api/export/*</c> do crivonft. Pode rodar de novo: o upsert por data evita duplicar.</summary>
    public static async Task<int> ImportarCrivonftAsync(IServiceProvider services, DateOnly? desde)
    {
        await using var scope = services.CreateAsyncScope();
        var crivonft = scope.ServiceProvider.GetRequiredService<ICrivonftClient>();
        if (!crivonft.ExportConfigurado)
        {
            Console.Error.WriteLine("Configure Integrations:Crivonft:BaseUrl e ExportToken antes de importar.");
            return 1;
        }
        var servico = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
        var e = await servico.CargaInicialAsync(desde, CancellationToken.None);
        Console.WriteLine($"Importação {(e.Estado == EstadoReconciliacao.Concluida ? "concluída" : "falhou")}: {e.VendasLidas} vendas lidas, {e.VendasAplicadas} aplicadas, {e.ClientesLidos} clientes lidos, {e.ClientesCriados} criados.");
        if (e.Erro is not null) Console.Error.WriteLine(e.Erro);
        return e.Estado == EstadoReconciliacao.Concluida ? 0 : 1;
    }
}
