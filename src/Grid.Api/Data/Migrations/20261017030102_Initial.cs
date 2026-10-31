using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grid.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "otp_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    app = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    request_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    sms_provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    sms_provider_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consumers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    phone_network = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    profile_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kyc_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kyc_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kyc_provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    kyc_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kyc_attempts = table.Column<int>(type: "integer", nullable: false),
                    kyc_next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kyc_last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    default_property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    first_seen_app = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_login_app = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nickname = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ghana_post_gps = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_properties", x => x.id);
                    table.ForeignKey(
                        name: "fk_properties_consumers_owner_consumer_id",
                        column: x => x.owner_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    app = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_consumers_consumer_id",
                        column: x => x.consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "utility_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    account_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nickname = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utility_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_utility_accounts_consumers_owner_consumer_id",
                        column: x => x.owner_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_utility_accounts_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumers_default_property_id",
                table: "consumers",
                column: "default_property_id");

            migrationBuilder.CreateIndex(
                name: "ix_consumers_kyc_status_kyc_next_attempt_at",
                table: "consumers",
                columns: new[] { "kyc_status", "kyc_next_attempt_at" },
                filter: "kyc_status IN ('NotChecked', 'Pending')");

            migrationBuilder.CreateIndex(
                name: "ix_consumers_phone_number",
                table: "consumers",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_phone_number_created_at",
                table: "otp_challenges",
                columns: new[] { "phone_number", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_properties_owner_consumer_id",
                table: "properties",
                column: "owner_consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_consumer_id",
                table: "refresh_tokens",
                column: "consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_utility_accounts_one_default_per_type",
                table: "utility_accounts",
                columns: new[] { "owner_consumer_id", "type" },
                unique: true,
                filter: "is_default AND archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_utility_accounts_owner_consumer_id_type_account_number",
                table: "utility_accounts",
                columns: new[] { "owner_consumer_id", "type", "account_number" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_utility_accounts_property_id",
                table: "utility_accounts",
                column: "property_id");

            migrationBuilder.AddForeignKey(
                name: "fk_consumers_properties_default_property_id",
                table: "consumers",
                column: "default_property_id",
                principalTable: "properties",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_consumers_properties_default_property_id",
                table: "consumers");

            migrationBuilder.DropTable(
                name: "otp_challenges");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "utility_accounts");

            migrationBuilder.DropTable(
                name: "properties");

            migrationBuilder.DropTable(
                name: "consumers");
        }
    }
}
