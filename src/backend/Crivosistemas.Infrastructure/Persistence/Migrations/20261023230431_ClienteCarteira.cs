using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClienteCarteira : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Carteira",
                table: "Clientes",
                type: "varchar(42)",
                maxLength: 42,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Carteira",
                table: "Clientes",
                column: "Carteira");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clientes_Carteira",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Carteira",
                table: "Clientes");
        }
    }
}
