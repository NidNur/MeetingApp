using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingApp.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentDeletionQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentDeletionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentDeletionJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDeletionJobs_NextAttemptAtUtc_Id",
                table: "DocumentDeletionJobs",
                columns: new[] { "NextAttemptAtUtc", "Id" });

            // Recover documents of previously deleted meetings, without touching active attachments.
            migrationBuilder.Sql("""
                INSERT INTO [DocumentDeletionJobs]
                    ([Id], [DocumentPath], [CreatedAtUtc], [NextAttemptAtUtc], [AttemptCount])
                SELECT NEWID(), paths.[DocumentPath], SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM (
                    SELECT DISTINCT logs.[DocumentPath]
                    FROM [MeetingDeletionLogs] logs
                    WHERE logs.[DocumentPath] IS NOT NULL
                        AND NOT EXISTS (
                            SELECT 1 FROM [Meetings] meetings
                            WHERE meetings.[DocumentPath] = logs.[DocumentPath])
                ) paths;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentDeletionJobs");
        }
    }
}
