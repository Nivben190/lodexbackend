using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Looxdex.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClosetItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Season = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Formality = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: true),
                    EmbeddedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosetItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Photographer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PhotographerUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Query = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Likes = table.Column<int>(type: "INTEGER", nullable: false),
                    AspectRatioWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    AspectRatioHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectionState = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DetectionAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    IngestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Rank = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedPosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suitcases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TripName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CoverImageUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpectedTempLow = table.Column<double>(type: "REAL", nullable: false),
                    ExpectedTempHigh = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suitcases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DetectedItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeedPostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LabelHe = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    BoxX = table.Column<double>(type: "REAL", nullable: false),
                    BoxY = table.Column<double>(type: "REAL", nullable: false),
                    BoxWidth = table.Column<double>(type: "REAL", nullable: false),
                    BoxHeight = table.Column<double>(type: "REAL", nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetectedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DetectedItems_FeedPosts_FeedPostId",
                        column: x => x.FeedPostId,
                        principalTable: "FeedPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FeedPostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedPosts_FeedPosts_FeedPostId",
                        column: x => x.FeedPostId,
                        principalTable: "FeedPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SuitcaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    EventLabel = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventGroups_Suitcases_SuitcaseId",
                        column: x => x.SuitcaseId,
                        principalTable: "Suitcases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingAlternatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DetectedItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    StoreUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingAlternatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingAlternatives_DetectedItems_DetectedItemId",
                        column: x => x.DetectedItemId,
                        principalTable: "DetectedItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackingItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClosetItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsPacked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingItems_EventGroups_EventGroupId",
                        column: x => x.EventGroupId,
                        principalTable: "EventGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClosetItems_OwnerKey",
                table: "ClosetItems",
                column: "OwnerKey");

            migrationBuilder.CreateIndex(
                name: "IX_DetectedItems_FeedPostId",
                table: "DetectedItems",
                column: "FeedPostId");

            migrationBuilder.CreateIndex(
                name: "IX_EventGroups_SuitcaseId",
                table: "EventGroups",
                column: "SuitcaseId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedPosts_DetectionState",
                table: "FeedPosts",
                column: "DetectionState");

            migrationBuilder.CreateIndex(
                name: "IX_FeedPosts_Rank",
                table: "FeedPosts",
                column: "Rank",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_FeedPosts_Source_ExternalId",
                table: "FeedPosts",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackingItems_EventGroupId",
                table: "PackingItems",
                column: "EventGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPosts_FeedPostId",
                table: "SavedPosts",
                column: "FeedPostId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPosts_OwnerKey_FeedPostId",
                table: "SavedPosts",
                columns: new[] { "OwnerKey", "FeedPostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingAlternatives_DetectedItemId",
                table: "ShoppingAlternatives",
                column: "DetectedItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Suitcases_OwnerKey",
                table: "Suitcases",
                column: "OwnerKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClosetItems");

            migrationBuilder.DropTable(
                name: "PackingItems");

            migrationBuilder.DropTable(
                name: "SavedPosts");

            migrationBuilder.DropTable(
                name: "ShoppingAlternatives");

            migrationBuilder.DropTable(
                name: "EventGroups");

            migrationBuilder.DropTable(
                name: "DetectedItems");

            migrationBuilder.DropTable(
                name: "Suitcases");

            migrationBuilder.DropTable(
                name: "FeedPosts");
        }
    }
}
