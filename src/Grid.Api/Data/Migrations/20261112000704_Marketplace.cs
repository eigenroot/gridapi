using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grid.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Marketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "job_number_seq",
                startValue: 1001L);

            migrationBuilder.CreateSequence<int>(
                name: "provider_payout_number_seq",
                startValue: 1001L);

            migrationBuilder.CreateSequence<int>(
                name: "service_request_number_seq",
                startValue: 1001L);

            migrationBuilder.CreateSequence<int>(
                name: "support_ticket_number_seq",
                startValue: 1001L);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "service_areas",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "service_areas",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "radius_km",
                table: "service_areas",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "base_latitude",
                table: "provider_accounts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "base_longitude",
                table: "provider_accounts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "location_updated_at",
                table: "provider_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "travel_radius_km",
                table: "provider_accounts",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "properties",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "properties",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_area_id",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "provider_payouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('provider_payout_number_seq')"),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    destination = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_by_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_payouts", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_payouts_consumers_recorded_by_consumer_id",
                        column: x => x.recorded_by_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_payouts_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('service_request_number_seq')"),
                    customer_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_service_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    items = table.Column<List<string>>(type: "text[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    units = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    urgency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ghana_post_gps = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    service_area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dispatch_round = table.Column<int>(type: "integer", nullable: false),
                    next_dispatch_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_requests_consumers_customer_consumer_id",
                        column: x => x.customer_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_requests_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_service_requests_service_areas_service_area_id",
                        column: x => x.service_area_id,
                        principalTable: "service_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_requests_service_categories_service_category_id",
                        column: x => x.service_category_id,
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_requests_service_categories_sub_service_category_id",
                        column: x => x.sub_service_category_id,
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    distance_km = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decline_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_offers_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_offers_service_requests_service_request_id",
                        column: x => x.service_request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('job_number_seq')"),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    fee_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    net = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    distance_km = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    work_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_confirm_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_jobs_consumers_customer_consumer_id",
                        column: x => x.customer_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_job_offers_job_offer_id",
                        column: x => x.job_offer_id,
                        principalTable: "job_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_service_requests_service_request_id",
                        column: x => x.service_request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_consumer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_events_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_earnings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gross = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    net = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_earnings", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_earnings_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_earnings_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_earnings_provider_payouts_payout_id",
                        column: x => x.payout_id,
                        principalTable: "provider_payouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('support_ticket_number_seq')"),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    app = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_support_tickets_consumers_consumer_id",
                        column: x => x.consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_support_tickets_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("056f2abf-db4f-85c4-4c96-b56ce32d9176"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.0846999999999998, -1.3509, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("08b93716-7138-0873-b668-035e0d916de1"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6959999999999997, -0.28299999999999997, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("0c3a0899-5990-d502-0b9a-c07b85a4ee1c"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 4.9189999999999996, -1.778, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("0d92428b-2db6-7c5c-f19d-e3440b12650f"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.7089999999999996, -0.16700000000000001, 3.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("114cfe5c-58f6-1f69-a2ec-6775b7ae13d7"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.04, -0.45000000000000001, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("17c1428b-4f18-29f0-618a-817598141532"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.5389999999999997, -0.26000000000000001, 3.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("1c447255-802f-0f99-d5e2-03da5b0d5a33"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.5789999999999997, -0.17399999999999999, 2.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("29467813-1b2f-4e8d-7cff-e15f68dda9f8"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 4.8970000000000002, -1.76, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("343eaa83-69e2-f89e-c997-fd07dcc2e811"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.5369999999999999, -0.69999999999999996, 5.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("37794a45-5fd5-1c82-379d-6942600e72ae"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.5510000000000002, -0.76600000000000001, 5.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("3c87b842-747b-929c-c1f2-e4774b3454e2"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.7400000000000002, -0.17499999999999999, 3.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("42543520-8cdd-d332-d5b8-0f3eb00b0b88"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.665, -1.6499999999999999, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("43169017-8a50-3bfd-6935-92653967a0bb"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 9.4350000000000005, -0.87, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("44b6ef07-4a90-d1dc-0680-144960df3e61"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.6660000000000004, -1.627, 2.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("4753cd2b-45d8-9773-9ce0-a57f07cb840f"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6829999999999998, -0.16600000000000001, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("4b10938c-8a27-6b02-0888-f602ed832ec9"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.351, -0.623, 5.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("5176605b-be7f-88e0-6ab2-7b3fdc33aa34"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.556, -0.33400000000000002, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("55a8bf80-50f4-0cf4-6b92-5d040d4e8d1b"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6500000000000004, -0.23599999999999999, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("5c4b0e80-38c3-ef4a-68ea-cb06f0be84a6"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6139999999999999, -0.22900000000000001, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("61c47d27-4f46-b894-6d1e-9a4ac498a30a"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.3010000000000002, -1.9930000000000001, 6.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("629d78bb-75df-af6d-c5c3-09e37a978ebf"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.8079999999999998, -0.34999999999999998, 5.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("6598b040-5196-0d17-abcb-6dc4406e22e7"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.2709999999999999, -1.0169999999999999, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("6873cf63-ff41-228e-036e-64ba3975986a"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 9.4034999999999993, -0.84230000000000005, 7.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("68f1d939-e406-37ee-be26-6bd2d9f04583"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.2960000000000003, 0.058999999999999997, 5.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("707c997a-fbe7-54ec-a466-54144bdd426f"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.7169999999999996, -1.6279999999999999, 2.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("7756ca89-0f2a-51c5-a63a-f16324837c08"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.5830000000000002, -0.105, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("7b5c5fef-515c-b868-2817-a51cfa1e959e"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 4.923, -1.7569999999999999, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("7b865800-7aa2-2157-92b7-fb8e113bedbb"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.5640000000000001, -0.17299999999999999, 2.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("7e328975-b5bd-bf3b-3df1-526e9dfda318"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6849999999999996, -0.13700000000000001, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("8951e815-59cb-f26b-372a-beaf7839bebe"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.2000000000000002, -1.6667000000000001, 6.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("8a40cc39-f368-dc6d-3b4e-2d3c8bf0c37e"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.6539999999999999, -1.595, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("8b7cc6d0-1b20-aa46-a01c-9a94f4e34d7b"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 9.6240000000000006, -0.82499999999999996, 5.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("92501b90-417a-24d0-1d30-f549a3ee1897"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.601, -0.076999999999999999, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("98b46b5e-6b30-fe6b-1aa4-37dd4394ca35"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6698000000000004, -0.0166, 6.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("a850cb4e-56cc-d37e-2492-6eaabda72d08"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.7329999999999997, -1.613, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("ad7bd408-862f-6fad-bd31-8d0ddf96b616"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.1052999999999997, -1.2465999999999999, 6.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("aded0a6e-ab7f-4a5f-46c8-9458b73049d9"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6360000000000001, -0.10100000000000001, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("aed82a9a-8535-4e98-29c2-8e538537d404"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 4.8959999999999999, -1.774, 2.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("b152c39e-45b4-4db6-42db-538f4458733a"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6070000000000002, -0.25, 2.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("b9263934-cebe-9dfd-a17f-b471c042495c"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6349999999999998, -0.157, 3.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("bf81eabf-9e4b-14b0-c6d8-3d23b7a53c81"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.0940000000000003, -0.25900000000000001, 6.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("cacb9eda-5242-d5b3-1653-ea5d0083deeb"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.673, -1.605, 2.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("d3d28f96-51e3-4be2-db33-ab87ba0c2847"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.7030000000000003, -1.637, 2.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("d732de1b-ace6-8ba5-0877-70c36d1910cc"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.556, -0.182, 2.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("de370065-1c06-2bf2-40ef-1bdf92cf67be"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.6050000000000004, -0.183, 2.5 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("de4e2fd7-20c6-44ed-9380-81b7bd61c5c9"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 4.9340000000000002, -1.704, 4.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("e3f346be-1030-55a3-7498-61a014c4f50f"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.6900000000000004, -1.6559999999999999, 3.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("e6370230-c790-c378-2caa-e20a6382e55c"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 9.4420000000000002, -0.01, 5.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("f7d79244-6838-bb30-2620-8ab08be589f6"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 6.7210000000000001, -1.47, 5.0 });

            migrationBuilder.UpdateData(
                table: "service_areas",
                keyColumn: "id",
                keyValue: new Guid("fff440c0-3bee-dbc7-9b0e-bc59ca924a51"),
                columns: new[] { "latitude", "longitude", "radius_km" },
                values: new object[] { 5.5339999999999998, -0.41699999999999998, 5.0 });

            migrationBuilder.CreateIndex(
                name: "ix_provider_accounts_operating_status_verification_status",
                table: "provider_accounts",
                columns: new[] { "operating_status", "verification_status" });

            migrationBuilder.CreateIndex(
                name: "ix_properties_service_area_id",
                table: "properties",
                column: "service_area_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_events_job_id_created_at",
                table: "job_events",
                columns: new[] { "job_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_provider_account_id_status_expires_at",
                table: "job_offers",
                columns: new[] { "provider_account_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_service_request_id_provider_account_id",
                table: "job_offers",
                columns: new[] { "service_request_id", "provider_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_auto_confirm_at",
                table: "jobs",
                column: "auto_confirm_at",
                filter: "stage = 'AwaitingConfirmation'");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_customer_consumer_id",
                table: "jobs",
                column: "customer_consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_job_offer_id",
                table: "jobs",
                column: "job_offer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_number",
                table: "jobs",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_one_live_per_request",
                table: "jobs",
                column: "service_request_id",
                unique: true,
                filter: "stage <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_provider_account_id_scheduled_at",
                table: "jobs",
                columns: new[] { "provider_account_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_earnings_job_id",
                table: "provider_earnings",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_earnings_payout_id",
                table: "provider_earnings",
                column: "payout_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_earnings_provider_account_id_status",
                table: "provider_earnings",
                columns: new[] { "provider_account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_payouts_number",
                table: "provider_payouts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_payouts_provider_account_id_created_at",
                table: "provider_payouts",
                columns: new[] { "provider_account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_payouts_recorded_by_consumer_id",
                table: "provider_payouts",
                column: "recorded_by_consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_customer_consumer_id_created_at",
                table: "service_requests",
                columns: new[] { "customer_consumer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_next_dispatch_at",
                table: "service_requests",
                column: "next_dispatch_at",
                filter: "status = 'Searching'");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_number",
                table: "service_requests",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_property_id",
                table: "service_requests",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_service_area_id",
                table: "service_requests",
                column: "service_area_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_service_category_id",
                table: "service_requests",
                column: "service_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_sub_service_category_id",
                table: "service_requests",
                column: "sub_service_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_consumer_id_created_at",
                table: "support_tickets",
                columns: new[] { "consumer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_job_id",
                table: "support_tickets",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_number",
                table: "support_tickets",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_provider_account_id",
                table: "support_tickets",
                column: "provider_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_status_created_at",
                table: "support_tickets",
                columns: new[] { "status", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_properties_service_areas_service_area_id",
                table: "properties",
                column: "service_area_id",
                principalTable: "service_areas",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Homes saved before areas had coordinates: place them by the area name they were given.
            migrationBuilder.Sql("""
                UPDATE properties p
                SET service_area_id = (
                    SELECT a.id FROM service_areas a
                    WHERE lower(a.name) = lower(btrim(p.area)) AND a.is_active
                    ORDER BY a.sort_order
                    LIMIT 1)
                WHERE p.service_area_id IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_properties_service_areas_service_area_id",
                table: "properties");

            migrationBuilder.DropTable(
                name: "job_events");

            migrationBuilder.DropTable(
                name: "provider_earnings");

            migrationBuilder.DropTable(
                name: "support_tickets");

            migrationBuilder.DropTable(
                name: "provider_payouts");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropTable(
                name: "job_offers");

            migrationBuilder.DropTable(
                name: "service_requests");

            migrationBuilder.DropIndex(
                name: "ix_provider_accounts_operating_status_verification_status",
                table: "provider_accounts");

            migrationBuilder.DropIndex(
                name: "ix_properties_service_area_id",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "service_areas");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "service_areas");

            migrationBuilder.DropColumn(
                name: "radius_km",
                table: "service_areas");

            migrationBuilder.DropColumn(
                name: "base_latitude",
                table: "provider_accounts");

            migrationBuilder.DropColumn(
                name: "base_longitude",
                table: "provider_accounts");

            migrationBuilder.DropColumn(
                name: "location_updated_at",
                table: "provider_accounts");

            migrationBuilder.DropColumn(
                name: "travel_radius_km",
                table: "provider_accounts");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "service_area_id",
                table: "properties");

            migrationBuilder.DropSequence(
                name: "job_number_seq");

            migrationBuilder.DropSequence(
                name: "provider_payout_number_seq");

            migrationBuilder.DropSequence(
                name: "service_request_number_seq");

            migrationBuilder.DropSequence(
                name: "support_ticket_number_seq");
        }
    }
}
