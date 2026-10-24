using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItensEstoqueUsd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "CustoUnitario",
                table: "ItensEstoque",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecoMercadoUsd",
                table: "ItensEstoque",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrecoMercadoUsd",
                table: "ItensEstoque");

            migrationBuilder.AlterColumn<decimal>(
                name: "CustoUnitario",
                table: "ItensEstoque",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);
        }
    }
}
