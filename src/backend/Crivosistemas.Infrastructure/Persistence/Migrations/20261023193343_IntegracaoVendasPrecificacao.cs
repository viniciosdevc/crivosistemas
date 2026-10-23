using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crivosistemas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegracaoVendasPrecificacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Chave = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Nickname = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true),
                    IdentidadeId = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    CriadoEmOrigem = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventosRecebidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    EventoId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    OcorridoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RecebidoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Estado = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    Corpo = table.Column<string>(type: "longtext", nullable: false),
                    Erro = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProcessadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Resultado = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Origem = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    OrigemId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ClienteChave = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosRecebidos", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FaixasPix",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ValorMinUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ValorMaxUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Multiplicador = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaixasPix", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PrecificacaoConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ValorKFeira = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ValorKTrocas = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MoedasPorK = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DolarPadraoCompra = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DolarPadraoVenda = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TaxaPrata = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MargemVendaCambiosPercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AtualizadoPor = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrecificacaoConfigs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PrecificacaoEnvios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    Estado = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProximaTentativaEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UltimaTentativaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EnviadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UltimoStatusHttp = table.Column<int>(type: "int", nullable: true),
                    UltimoErro = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrecificacaoEnvios", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PrecificacaoHistoricos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    UserNome = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Origem = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Antes = table.Column<string>(type: "json", nullable: false),
                    Depois = table.Column<string>(type: "json", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrecificacaoHistoricos", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Reconciliacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    De = table.Column<DateTime>(type: "date", nullable: true),
                    Ate = table.Column<DateTime>(type: "date", nullable: true),
                    Estado = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    SolicitadoPor = table.Column<Guid>(type: "char(36)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IniciadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConcluidoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    VendasLidas = table.Column<int>(type: "int", nullable: false),
                    VendasAplicadas = table.Column<int>(type: "int", nullable: false),
                    ClientesLidos = table.Column<int>(type: "int", nullable: false),
                    ClientesCriados = table.Column<int>(type: "int", nullable: false),
                    Erro = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reconciliacoes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Vendas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Origem = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    OrigemId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PedidoId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    DataHora = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    StatusNormalizado = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false),
                    FormaPagamento = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    Vendedor = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    ClienteId = table.Column<Guid>(type: "char(36)", nullable: true),
                    TotalBrl = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    DescontoBrl = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    TaxaBrl = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    ValorPixBrl = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    ValorCambios = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CustoBrl = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    CustoUsd = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CustoCambios = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LucroBrl = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    MargemPercent = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    CupomCodigo = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    CupomTipo = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true),
                    AtualizadoEmOrigem = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RemovidaEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vendas_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VendaItens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    VendaId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    Descricao = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PrecoUsd = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    PrecoBrl = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    Referencia = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendaItens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendaItens_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Chave",
                table: "Clientes",
                column: "Chave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Nickname",
                table: "Clientes",
                column: "Nickname");

            migrationBuilder.CreateIndex(
                name: "IX_EventosRecebidos_Estado_RecebidoEm",
                table: "EventosRecebidos",
                columns: new[] { "Estado", "RecebidoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_EventosRecebidos_EventoId",
                table: "EventosRecebidos",
                column: "EventoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosRecebidos_Origem_OrigemId",
                table: "EventosRecebidos",
                columns: new[] { "Origem", "OrigemId" });

            migrationBuilder.CreateIndex(
                name: "IX_EventosRecebidos_RecebidoEm",
                table: "EventosRecebidos",
                column: "RecebidoEm");

            migrationBuilder.CreateIndex(
                name: "IX_FaixasPix_Ordem",
                table: "FaixasPix",
                column: "Ordem");

            migrationBuilder.CreateIndex(
                name: "IX_PrecificacaoEnvios_Estado_ProximaTentativaEm",
                table: "PrecificacaoEnvios",
                columns: new[] { "Estado", "ProximaTentativaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_PrecificacaoEnvios_Versao",
                table: "PrecificacaoEnvios",
                column: "Versao");

            migrationBuilder.CreateIndex(
                name: "IX_PrecificacaoHistoricos_Versao",
                table: "PrecificacaoHistoricos",
                column: "Versao");

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliacoes_Estado_CriadoEm",
                table: "Reconciliacoes",
                columns: new[] { "Estado", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliacoes_Tipo_CriadoEm",
                table: "Reconciliacoes",
                columns: new[] { "Tipo", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_VendaItens_VendaId",
                table: "VendaItens",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_ClienteId_DataHora",
                table: "Vendas",
                columns: new[] { "ClienteId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_DataHora",
                table: "Vendas",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_Origem_OrigemId",
                table: "Vendas",
                columns: new[] { "Origem", "OrigemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_StatusNormalizado_DataHora",
                table: "Vendas",
                columns: new[] { "StatusNormalizado", "DataHora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosRecebidos");

            migrationBuilder.DropTable(
                name: "FaixasPix");

            migrationBuilder.DropTable(
                name: "PrecificacaoConfigs");

            migrationBuilder.DropTable(
                name: "PrecificacaoEnvios");

            migrationBuilder.DropTable(
                name: "PrecificacaoHistoricos");

            migrationBuilder.DropTable(
                name: "Reconciliacoes");

            migrationBuilder.DropTable(
                name: "VendaItens");

            migrationBuilder.DropTable(
                name: "Vendas");

            migrationBuilder.DropTable(
                name: "Clientes");
        }
    }
}
