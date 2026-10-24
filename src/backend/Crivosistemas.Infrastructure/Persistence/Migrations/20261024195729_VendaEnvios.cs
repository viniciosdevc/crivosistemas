using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VendaEnvios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VendaEnvios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    VendaId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Estado = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProximaTentativaEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UltimaTentativaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EnviadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EnviadoAtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UltimoStatusHttp = table.Column<int>(type: "int", nullable: true),
                    UltimoErro = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendaEnvios", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_VendaEnvios_Estado_ProximaTentativaEm",
                table: "VendaEnvios",
                columns: new[] { "Estado", "ProximaTentativaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_VendaEnvios_VendaId",
                table: "VendaEnvios",
                column: "VendaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VendaEnvios");
        }
    }
}
