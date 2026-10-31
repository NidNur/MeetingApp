using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingApp.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingDeletionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeetingDeletionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MeetingId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocumentPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DocumentOriginalName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsCancelled = table.Column<bool>(type: "bit", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingDeletionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingDeletionLogs_MeetingId",
                table: "MeetingDeletionLogs",
                column: "MeetingId");

            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER [dbo].[TR_Meetings_DeleteLog]
                ON [dbo].[Meetings]
                AFTER DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    INSERT INTO [dbo].[MeetingDeletionLogs]
                    (
                        [MeetingId], [Name], [Description], [StartsAtUtc], [EndsAtUtc],
                        [DocumentPath], [DocumentOriginalName], [IsCancelled], [CancelledAtUtc],
                        [CreatedAtUtc], [CreatedById], [DeletedAtUtc]
                    )
                    SELECT
                        [Id], [Name], [Description], [StartsAtUtc], [EndsAtUtc],
                        [DocumentPath], [DocumentOriginalName], [IsCancelled], [CancelledAtUtc],
                        [CreatedAtUtc], [CreatedById], SYSUTCDATETIME()
                    FROM deleted;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_Meetings_DeleteLog];");

            migrationBuilder.DropTable(
                name: "MeetingDeletionLogs");
        }
    }
}
