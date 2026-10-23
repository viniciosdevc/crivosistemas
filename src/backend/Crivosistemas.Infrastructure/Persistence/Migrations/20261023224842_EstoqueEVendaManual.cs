using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstoqueEVendaManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CriadoPor",
                table: "Vendas",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DolarCompraUsado",
                table: "Vendas",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacao",
                table: "Vendas",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemCusto",
                table: "Vendas",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantidadeEsmeraldas",
                table: "Vendas",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsaTaxaPrata",
                table: "Vendas",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConfiguracoesEstoque",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CustoMedioUsdFixo = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesEstoque", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LotesEstoque",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Origem = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    OrigemId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    DataCompra = table.Column<DateTime>(type: "date", nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Saldo = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CustoTotal = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CustoUnitario = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Observacao = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    CriadoPor = table.Column<Guid>(type: "char(36)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CanceladoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesEstoque", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MovimentosEstoque",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    LoteId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CustoUnitario = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Motivo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    VendaId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EstornadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Observacao = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosEstoque", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentosEstoque_LotesEstoque_LoteId",
                        column: x => x.LoteId,
                        principalTable: "LotesEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LotesEstoque_Tipo_DataCompra_CriadoEm",
                table: "LotesEstoque",
                columns: new[] { "Tipo", "DataCompra", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_LotesEstoque_Tipo_OrigemId",
                table: "LotesEstoque",
                columns: new[] { "Tipo", "OrigemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosEstoque_LoteId",
                table: "MovimentosEstoque",
                column: "LoteId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosEstoque_Tipo_CriadoEm",
                table: "MovimentosEstoque",
                columns: new[] { "Tipo", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosEstoque_VendaId",
                table: "MovimentosEstoque",
                column: "VendaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracoesEstoque");

            migrationBuilder.DropTable(
                name: "MovimentosEstoque");

            migrationBuilder.DropTable(
                name: "LotesEstoque");

            migrationBuilder.DropColumn(
                name: "CriadoPor",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "DolarCompraUsado",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "Observacao",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "OrigemCusto",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "QuantidadeEsmeraldas",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "UsaTaxaPrata",
                table: "Vendas");
        }
    }
}
