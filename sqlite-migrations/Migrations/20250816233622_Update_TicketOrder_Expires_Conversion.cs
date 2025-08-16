using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CertsServer.Migrations
{
    /// <inheritdoc />
    public partial class Update_TicketOrder_Expires_Conversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "Expires",
                table: "ticket_orders",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "Expires",
                table: "ticket_orders",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
