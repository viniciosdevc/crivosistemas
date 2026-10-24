using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Financeiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LancamentosFinanceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    Origem = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    Categoria = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Descricao = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    ValorBrl = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    Data = table.Column<DateTime>(type: "date", nullable: false),
                    VendaId = table.Column<Guid>(type: "char(36)", nullable: true),
                    LoteEstoqueId = table.Column<Guid>(type: "char(36)", nullable: true),
                    MovimentoCambiosId = table.Column<Guid>(type: "char(36)", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    CriadoPor = table.Column<Guid>(type: "char(36)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConfirmadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LancamentosFinanceiros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_LotesEstoque_LoteEstoqueId",
                        column: x => x.LoteEstoqueId,
                        principalTable: "LotesEstoque",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_MovimentosCambios_MovimentoCambiosId",
                        column: x => x.MovimentoCambiosId,
                        principalTable: "MovimentosCambios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_LoteEstoqueId",
                table: "LancamentosFinanceiros",
                column: "LoteEstoqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_MovimentoCambiosId",
                table: "LancamentosFinanceiros",
                column: "MovimentoCambiosId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_Status_Data",
                table: "LancamentosFinanceiros",
                columns: new[] { "Status", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_Tipo_Data",
                table: "LancamentosFinanceiros",
                columns: new[] { "Tipo", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_VendaId",
                table: "LancamentosFinanceiros",
                column: "VendaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LancamentosFinanceiros");
        }
    }
}
