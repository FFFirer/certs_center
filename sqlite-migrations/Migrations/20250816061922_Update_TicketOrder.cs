using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CertsServer.Migrations
{
    /// <inheritdoc />
    public partial class Update_TicketOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Expires",
                table: "ticket_orders",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Expires",
                table: "ticket_orders");
        }
    }
}
