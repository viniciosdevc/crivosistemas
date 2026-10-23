using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crivosistemas.Infrastructure.Persistence;

/// <summary>Usado só pelo <c>dotnet ef</c>. A conexão vem de CRIVOSISTEMAS_DB, com o padrão local como alternativa.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CRIVOSISTEMAS_DB")
            ?? "Server=localhost;Port=3306;Database=crivosistemas;User=crivosistemas;Password=crivosistemas;";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseMySQL(cs).Options;
        return new AppDbContext(options);
    }
}
