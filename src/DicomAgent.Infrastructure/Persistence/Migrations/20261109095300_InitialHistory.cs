using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DicomAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageRoot = table.Column<string>(type: "TEXT", nullable: false),
                    SopInstanceUid = table.Column<string>(type: "TEXT", nullable: false),
                    StudyInstanceUid = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesInstanceUid = table.Column<string>(type: "TEXT", nullable: false),
                    SopClassUid = table.Column<string>(type: "TEXT", nullable: false),
                    TransferSyntaxUid = table.Column<string>(type: "TEXT", nullable: false),
                    PatientId = table.Column<string>(type: "TEXT", nullable: true),
                    PatientName = table.Column<string>(type: "TEXT", nullable: true),
                    StudyDate = table.Column<string>(type: "TEXT", nullable: true),
                    StudyTime = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesNumber = table.Column<string>(type: "TEXT", nullable: true),
                    InstanceNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Modality = table.Column<string>(type: "TEXT", nullable: true),
                    Rows = table.Column<int>(type: "INTEGER", nullable: true),
                    Columns = table.Column<int>(type: "INTEGER", nullable: true),
                    BitsAllocated = table.Column<int>(type: "INTEGER", nullable: true),
                    BitsStored = table.Column<int>(type: "INTEGER", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_ReceivedAtUtc",
                table: "Instances",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_StorageRoot_SopInstanceUid",
                table: "Instances",
                columns: new[] { "StorageRoot", "SopInstanceUid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Instances");
        }
    }
}
