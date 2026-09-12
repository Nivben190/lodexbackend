using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Looxdex.Api.Migrations
{
    /// <inheritdoc />
    public partial class WishlistAndSavedFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Folder",
                table: "SavedPosts",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWishlist",
                table: "ClosetItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Folder",
                table: "SavedPosts");

            migrationBuilder.DropColumn(
                name: "IsWishlist",
                table: "ClosetItems");
        }
    }
}
