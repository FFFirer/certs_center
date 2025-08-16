using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CertsServer.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderUrl = table.Column<string>(type: "TEXT", nullable: false),
                    TicketId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedTime = table.Column<long>(type: "INTEGER", nullable: false),
                    LastUpdatedTime = table.Column<long>(type: "INTEGER", nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Certificate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DomainNames = table.Column<string>(type: "TEXT", nullable: true),
                    PfxPassword = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedTime = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedTime = table.Column<long>(type: "INTEGER", nullable: false),
                    Remark = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_orders");

            migrationBuilder.DropTable(
                name: "tickets");
        }
    }
}
