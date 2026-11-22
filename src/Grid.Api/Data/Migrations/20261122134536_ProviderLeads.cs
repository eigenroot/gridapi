using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grid.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProviderLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "provider_lead_number_seq",
                startValue: 1001L);

            migrationBuilder.CreateTable(
                name: "provider_leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('provider_lead_number_seq')"),
                    phone_number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    services = table.Column<List<string>>(type: "text[]", nullable: false),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    experience = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    medium = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    campaign = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sms_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sms_provider_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signed_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    contacted_by_consumer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_leads_consumers_consumer_id",
                        column: x => x.consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_provider_leads_consumers_contacted_by_consumer_id",
                        column: x => x.contacted_by_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_provider_leads_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_leads_consumer_id",
                table: "provider_leads",
                column: "consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_leads_contacted_by_consumer_id",
                table: "provider_leads",
                column: "contacted_by_consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_leads_created_at",
                table: "provider_leads",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_provider_leads_number",
                table: "provider_leads",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_leads_phone_number",
                table: "provider_leads",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_leads_provider_account_id",
                table: "provider_leads",
                column: "provider_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_leads");

            migrationBuilder.DropSequence(
                name: "provider_lead_number_seq");
        }
    }
}
