using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClienteCarteiraFavorito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Carteira",
                table: "Clientes",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(42)",
                oldMaxLength: 42,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarteiraLoja",
                table: "Clientes",
                type: "varchar(42)",
                maxLength: 42,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Favorito",
                table: "Clientes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Favorito",
                table: "Clientes",
                column: "Favorito");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clientes_Favorito",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "CarteiraLoja",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Favorito",
                table: "Clientes");

            migrationBuilder.AlterColumn<string>(
                name: "Carteira",
                table: "Clientes",
                type: "varchar(42)",
                maxLength: 42,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(120)",
                oldMaxLength: 120,
                oldNullable: true);
        }
    }
}
