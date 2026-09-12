using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Looxdex.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGarmentCutouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color_hex",
                table: "detected_items",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color_name",
                table: "detected_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cutout_image_id",
                table: "detected_items",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color_hex",
                table: "detected_items");

            migrationBuilder.DropColumn(
                name: "color_name",
                table: "detected_items");

            migrationBuilder.DropColumn(
                name: "cutout_image_id",
                table: "detected_items");
        }
    }
}
