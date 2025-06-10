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
                name: "CertificateFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    NotAfter = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    NotBefore = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DomainNameActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainNameActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DomainNames",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CertificateFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainNames", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificateFiles");

            migrationBuilder.DropTable(
                name: "DomainNameActions");

            migrationBuilder.DropTable(
                name: "DomainNames");
        }
    }
}
