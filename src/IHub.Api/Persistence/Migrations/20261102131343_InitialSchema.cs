using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IHub.Api.Persistence.Migrations
{
    public partial class InitialSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RCSI: statement reads do not block or get blocked by ingestion. Cannot run inside a transaction.
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceSystem = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceActivityId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IntermediaryId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActivityType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    FirstReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastCorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.CheckConstraint("CK_Activities_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_Activities_Status", "[Status] IN ('Active', 'Reversed')");
                    table.CheckConstraint("CK_Activities_Version", "[Version] >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_Intermediary_OccurredAt",
                table: "Activities",
                columns: new[] { "IntermediaryId", "OccurredAt" })
                .Annotation("SqlServer:Include", new[] { "Status", "Currency", "Amount", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Activities_SourceKey",
                table: "Activities",
                columns: new[] { "SourceSystem", "SourceActivityId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Activities");
        }
    }
}
