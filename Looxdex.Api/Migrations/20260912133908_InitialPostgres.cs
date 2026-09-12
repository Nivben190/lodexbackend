using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Looxdex.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "closet_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    color_hex = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    season = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    brand = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    formality = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    is_favorite = table.Column<bool>(type: "boolean", nullable: false),
                    is_wishlist = table.Column<bool>(type: "boolean", nullable: false),
                    added_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    embedding = table.Column<byte[]>(type: "bytea", nullable: true),
                    embedded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_closet_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feed_posts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source = table.Column<int>(type: "integer", nullable: false),
                    external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    thumbnail_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    photographer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    photographer_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    source_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    query = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    likes = table.Column<int>(type: "integer", nullable: false),
                    aspect_ratio_width = table.Column<int>(type: "integer", nullable: false),
                    aspect_ratio_height = table.Column<int>(type: "integer", nullable: false),
                    detection_state = table.Column<int>(type: "integer", nullable: false),
                    detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    detection_attempts = table.Column<int>(type: "integer", nullable: false),
                    ingested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rank = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_posts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "owner_profiles",
                columns: table => new
                {
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    seeded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owner_profiles", x => x.owner_key);
                });

            migrationBuilder.CreateTable(
                name: "suitcases",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trip_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cover_image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expected_temp_low = table.Column<double>(type: "double precision", nullable: false),
                    expected_temp_high = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suitcases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "uploaded_images",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uploaded_images", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "detected_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    feed_post_id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label_he = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    box_x = table.Column<double>(type: "double precision", nullable: false),
                    box_y = table.Column<double>(type: "double precision", nullable: false),
                    box_width = table.Column<double>(type: "double precision", nullable: false),
                    box_height = table.Column<double>(type: "double precision", nullable: false),
                    embedding = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_detected_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_detected_items_feed_posts_feed_post_id",
                        column: x => x.feed_post_id,
                        principalTable: "feed_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saved_posts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    feed_post_id = table.Column<int>(type: "integer", nullable: false),
                    folder = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    saved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_posts", x => x.id);
                    table.ForeignKey(
                        name: "fk_saved_posts_feed_posts_feed_post_id",
                        column: x => x.feed_post_id,
                        principalTable: "feed_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_groups",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    suitcase_id = table.Column<int>(type: "integer", nullable: false),
                    event_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    event_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_groups_suitcases_suitcase_id",
                        column: x => x.suitcase_id,
                        principalTable: "suitcases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shopping_alternatives",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    detected_item_id = table.Column<int>(type: "integer", nullable: false),
                    brand = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    store_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopping_alternatives", x => x.id);
                    table.ForeignKey(
                        name: "fk_shopping_alternatives_detected_items_detected_item_id",
                        column: x => x.detected_item_id,
                        principalTable: "detected_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "packing_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_group_id = table.Column<int>(type: "integer", nullable: false),
                    closet_item_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_packed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packing_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_packing_items_event_groups_event_group_id",
                        column: x => x.event_group_id,
                        principalTable: "event_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_closet_items_owner_key",
                table: "closet_items",
                column: "owner_key");

            migrationBuilder.CreateIndex(
                name: "ix_detected_items_feed_post_id",
                table: "detected_items",
                column: "feed_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_groups_suitcase_id",
                table: "event_groups",
                column: "suitcase_id");

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_detection_state",
                table: "feed_posts",
                column: "detection_state");

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_rank",
                table: "feed_posts",
                column: "rank",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_feed_posts_source_external_id",
                table: "feed_posts",
                columns: new[] { "source", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_packing_items_event_group_id",
                table: "packing_items",
                column: "event_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_posts_feed_post_id",
                table: "saved_posts",
                column: "feed_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_posts_owner_key_feed_post_id",
                table: "saved_posts",
                columns: new[] { "owner_key", "feed_post_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shopping_alternatives_detected_item_id",
                table: "shopping_alternatives",
                column: "detected_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_suitcases_owner_key",
                table: "suitcases",
                column: "owner_key");

            migrationBuilder.CreateIndex(
                name: "ix_uploaded_images_owner_key",
                table: "uploaded_images",
                column: "owner_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "closet_items");

            migrationBuilder.DropTable(
                name: "owner_profiles");

            migrationBuilder.DropTable(
                name: "packing_items");

            migrationBuilder.DropTable(
                name: "saved_posts");

            migrationBuilder.DropTable(
                name: "shopping_alternatives");

            migrationBuilder.DropTable(
                name: "uploaded_images");

            migrationBuilder.DropTable(
                name: "event_groups");

            migrationBuilder.DropTable(
                name: "detected_items");

            migrationBuilder.DropTable(
                name: "suitcases");

            migrationBuilder.DropTable(
                name: "feed_posts");
        }
    }
}
