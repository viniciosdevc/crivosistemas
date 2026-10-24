using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItensEstoque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItensEstoque",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Categoria = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Nome = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ImagemUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    CustoUnitario = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DataCompra = table.Column<DateTime>(type: "date", nullable: false),
                    PrecoMercadoBrl = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PrecoAtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Observacao = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ValorVenda = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DataVenda = table.Column<DateTime>(type: "date", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    CriadoPor = table.Column<Guid>(type: "char(36)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensEstoque", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ItensEstoque_Status_DataCompra",
                table: "ItensEstoque",
                columns: new[] { "Status", "DataCompra" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensEstoque");
        }
    }
}
