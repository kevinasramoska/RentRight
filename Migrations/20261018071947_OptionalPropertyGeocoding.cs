using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentRight.Migrations
{
    /// <inheritdoc />
    public partial class OptionalPropertyGeocoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GeocodedUtc",
                table: "Properties",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeocodingPlaceId",
                table: "Properties",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Properties",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Properties",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Properties_Location",
                table: "Properties",
                sql: "([Latitude] IS NULL AND [Longitude] IS NULL AND [GeocodingPlaceId] IS NULL AND [GeocodedUtc] IS NULL) OR ([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL AND [GeocodingPlaceId] IS NOT NULL AND LEN([GeocodingPlaceId]) > 0 AND [GeocodedUtc] IS NOT NULL AND [Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Properties_Location",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "GeocodedUtc",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "GeocodingPlaceId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Properties");
        }
    }
}
