using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceiroInicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracoesFinanceiro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DataInicio = table.Column<DateTime>(type: "date", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesFinanceiro", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            // O financeiro passa a contar a partir de 24/10/2026. O que a primeira versão sincronizou do histórico de vendas e compras
            // de dólar (dias anteriores) é cancelado, sem apagar, e sai do saldo.
            migrationBuilder.Sql("INSERT INTO `ConfiguracoesFinanceiro` (`Id`, `DataInicio`, `AtualizadoEm`) VALUES (UUID(), '2026-10-24', UTC_TIMESTAMP(6));");
            migrationBuilder.Sql("UPDATE `LancamentosFinanceiros` SET `Status` = 'Cancelado', `MotivoCancelamento` = 'Anterior ao início do controle financeiro.', `CanceladoEm` = UTC_TIMESTAMP(6), `AtualizadoEm` = UTC_TIMESTAMP(6) WHERE `Origem` <> 'Manual' AND `Status` <> 'Cancelado' AND `Data` < '2026-10-24';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracoesFinanceiro");
        }
    }
}
