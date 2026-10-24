using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosicaoAjustes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AjusteBrl",
                table: "PosicoesDiarias",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AjusteUsd",
                table: "PosicoesDiarias",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AjusteCambios",
                table: "PosicoesDiarias",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AjusteEsmeraldas",
                table: "PosicoesDiarias",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrlFinal",
                table: "PosicoesDiarias",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UsdFinal",
                table: "PosicoesDiarias",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CambiosFinal",
                table: "PosicoesDiarias",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EsmeraldasFinal",
                table: "PosicoesDiarias",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecoUsd",
                table: "PosicoesDiarias",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BrlPorCambio",
                table: "PosicoesDiarias",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BrlPorEsmeralda",
                table: "PosicoesDiarias",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AjusteBrl",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "AjusteUsd",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "AjusteCambios",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "AjusteEsmeraldas",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "BrlFinal",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "UsdFinal",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "CambiosFinal",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "EsmeraldasFinal",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "PrecoUsd",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "BrlPorCambio",
                table: "PosicoesDiarias");

            migrationBuilder.DropColumn(
                name: "BrlPorEsmeralda",
                table: "PosicoesDiarias");
        }
    }
}
