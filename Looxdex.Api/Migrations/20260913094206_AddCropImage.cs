using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Looxdex.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCropImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "crop_image_id",
                table: "detected_items",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "crop_image_id",
                table: "detected_items");
        }
    }
}
