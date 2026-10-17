using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRight.Migrations
{
    /// <inheritdoc />
    public partial class MeasuredQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaintenanceRequests_TenancyId_DateSubmitted",
                table: "MaintenanceRequests");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Title_Address_Id",
                table: "Properties",
                columns: new[] { "Title", "Address", "Id" })
                .Annotation("SqlServer:Include", new[] { "RentAmount", "Description", "LandlordId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_Status_DateSubmitted_Id",
                table: "MaintenanceRequests",
                columns: new[] { "Status", "DateSubmitted", "Id" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "TenancyId", "Priority", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_TenancyId_DateSubmitted",
                table: "MaintenanceRequests",
                columns: new[] { "TenancyId", "DateSubmitted" })
                .Annotation("SqlServer:Include", new[] { "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Properties_Title_Address_Id",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceRequests_Status_DateSubmitted_Id",
                table: "MaintenanceRequests");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceRequests_TenancyId_DateSubmitted",
                table: "MaintenanceRequests");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_TenancyId_DateSubmitted",
                table: "MaintenanceRequests",
                columns: new[] { "TenancyId", "DateSubmitted" });
        }
    }
}
