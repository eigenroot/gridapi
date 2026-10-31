using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Grid.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProviderOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_objects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    declared_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    upload_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_objects", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_objects_consumers_owner_consumer_id",
                        column: x => x.owner_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    business_registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    primary_area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    years_of_experience = table.Column<int>(type: "integer", nullable: false),
                    delivery_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    premises_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    operating_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_accounts_consumers_owner_consumer_id",
                        column: x => x.owner_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_areas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    default_pricing_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_categories_service_categories_parent_category_id",
                        column: x => x.parent_category_id,
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payout_methods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    account_number_encrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    account_number_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    masked_account_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_methods", x => x.id);
                    table.ForeignKey(
                        name: "fk_payout_methods_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    current_step = table.Column<int>(type: "integer", nullable: false),
                    main_skill = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    secondary_skills = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tools_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    certifications = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    experience_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    years_operating = table.Column<int>(type: "integer", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_applications_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_availability_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_availability_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_availability_rules_provider_accounts_provider_acco",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_standards_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    standards_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    accepted_by_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_standards_acceptances", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_standards_acceptances_consumers_accepted_by_consum",
                        column: x => x.accepted_by_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_standards_acceptances_provider_accounts_provider_a",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_verification_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    media_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    review_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_verification_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_verification_documents_media_objects_media_object_",
                        column: x => x.media_object_id,
                        principalTable: "media_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_verification_documents_provider_accounts_provider_",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_service_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    maximum_travel_distance_km = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    supports_emergency = table.Column<bool>(type: "boolean", nullable: false),
                    supports_same_day = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_service_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_service_areas_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_provider_service_areas_service_areas_service_area_id",
                        column: x => x.service_area_id,
                        principalTable: "service_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_offerings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_service_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    pricing_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fixed_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    starting_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    inspection_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    unit_name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    supports_emergency = table.Column<bool>(type: "boolean", nullable: false),
                    supports_same_day = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_offerings", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_offerings_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_provider_offerings_service_categories_service_category_id",
                        column: x => x.service_category_id,
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_portfolio_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    media_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    project_completed_at = table.Column<DateOnly>(type: "date", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_portfolio_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_portfolio_items_media_objects_media_object_id",
                        column: x => x.media_object_id,
                        principalTable: "media_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_portfolio_items_provider_accounts_provider_account",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_provider_portfolio_items_service_categories_service_categor",
                        column: x => x.service_category_id,
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "provider_application_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_application_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_application_submissions_provider_applications_prov",
                        column: x => x.provider_application_id,
                        principalTable: "provider_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "service_areas",
                columns: new[] { "id", "created_at", "is_active", "name", "region", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { new Guid("056f2abf-db4f-85c4-4c96-b56ce32d9176"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Elmina", "Central", 38, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("08b93716-7138-0873-b668-035e0d916de1"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Pokuase", "Greater Accra", 20, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0c3a0899-5990-d502-0b9a-c07b85a4ee1c"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Anaji", "Western", 33, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0d92428b-2db6-7c5c-f19d-e3440b12650f"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Adenta", "Greater Accra", 3, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("114cfe5c-58f6-1f69-a2ec-6775b7ae13d7"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Suhum", "Eastern", 44, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("17c1428b-4f18-29f0-618a-817598141532"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Dansoman", "Greater Accra", 10, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("1c447255-802f-0f99-d5e2-03da5b0d5a33"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Cantonments", "Greater Accra", 8, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("29467813-1b2f-4e8d-7cff-e15f68dda9f8"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Takoradi", "Western", 31, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("343eaa83-69e2-f89e-c997-fd07dcc2e811"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Swedru", "Central", 41, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("37794a45-5fd5-1c82-379d-6942600e72ae"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Nkawkaw", "Eastern", 46, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("3c87b842-747b-929c-c1f2-e4774b3454e2"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Oyarifa", "Greater Accra", 19, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("42543520-8cdd-d332-d5b8-0f3eb00b0b88"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Santasi", "Ashanti", 27, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("43169017-8a50-3bfd-6935-92653967a0bb"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Sagnarigu", "Northern", 48, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("44b6ef07-4a90-d1dc-0680-144960df3e61"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Ahodwo", "Ashanti", 21, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("4753cd2b-45d8-9773-9ce0-a57f07cb840f"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Madina", "Greater Accra", 4, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("4b10938c-8a27-6b02-0888-f602ed832ec9"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Winneba", "Central", 40, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("5176605b-be7f-88e0-6ab2-7b3fdc33aa34"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Weija", "Greater Accra", 18, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("55a8bf80-50f4-0cf4-6b92-5d040d4e8d1b"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Dome", "Greater Accra", 13, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("5c4b0e80-38c3-ef4a-68ea-cb06f0be84a6"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Achimota", "Greater Accra", 11, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("61c47d27-4f46-b894-6d1e-9a4ac498a30a"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Tarkwa", "Western", 36, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("629d78bb-75df-af6d-c5c3-09e37a978ebf"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Nsawam", "Eastern", 43, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("6598b040-5196-0d17-abcb-6dc4406e22e7"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Mankessim", "Central", 39, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("6873cf63-ff41-228e-036e-64ba3975986a"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Tamale", "Northern", 47, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("68f1d939-e406-37ee-be26-6bd2d9f04583"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Akosombo", "Eastern", 45, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("707c997a-fbe7-54ec-a466-54144bdd426f"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Suame", "Ashanti", 24, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("7756ca89-0f2a-51c5-a63a-f16324837c08"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Teshie", "Greater Accra", 15, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("7b5c5fef-515c-b868-2817-a51cfa1e959e"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Effia", "Western", 35, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("7b865800-7aa2-2157-92b7-fb8e113bedbb"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Labone", "Greater Accra", 7, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("7e328975-b5bd-bf3b-3df1-526e9dfda318"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Ashaley Botwe", "Greater Accra", 14, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("8951e815-59cb-f26b-372a-beaf7839bebe"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Obuasi", "Ashanti", 30, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("8a40cc39-f368-dc6d-3b4e-2d3c8bf0c37e"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Atonsu", "Ashanti", 29, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("8b7cc6d0-1b20-aa46-a01c-9a94f4e34d7b"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Savelugu", "Northern", 49, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("92501b90-417a-24d0-1d30-f549a3ee1897"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Nungua", "Greater Accra", 16, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("98b46b5e-6b30-fe6b-1aa4-37dd4394ca35"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Tema", "Greater Accra", 5, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a850cb4e-56cc-d37e-2492-6eaabda72d08"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Tafo", "Ashanti", 25, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("ad7bd408-862f-6fad-bd31-8d0ddf96b616"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Cape Coast", "Central", 37, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("aded0a6e-ab7f-4a5f-46c8-9458b73049d9"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Spintex", "Greater Accra", 2, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("aed82a9a-8535-4e98-29c2-8e538537d404"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Airport Ridge", "Western", 34, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b152c39e-45b4-4db6-42db-538f4458733a"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Lapaz", "Greater Accra", 12, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b9263934-cebe-9dfd-a17f-b471c042495c"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "East Legon", "Greater Accra", 1, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("bf81eabf-9e4b-14b0-c6d8-3d23b7a53c81"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Koforidua", "Eastern", 42, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("cacb9eda-5242-d5b3-1653-ea5d0083deeb"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Asokwa", "Ashanti", 22, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("d3d28f96-51e3-4be2-db33-ab87ba0c2847"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Bantama", "Ashanti", 23, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("d732de1b-ace6-8ba5-0877-70c36d1910cc"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Osu", "Greater Accra", 6, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("de370065-1c06-2bf2-40ef-1bdf92cf67be"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Airport Residential", "Greater Accra", 9, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("de4e2fd7-20c6-44ed-9380-81b7bd61c5c9"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Sekondi", "Western", 32, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("e3f346be-1030-55a3-7498-61a014c4f50f"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Kwadaso", "Ashanti", 28, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("e6370230-c790-c378-2caa-e20a6382e55c"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Yendi", "Northern", 50, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("f7d79244-6838-bb30-2620-8ab08be589f6"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Ejisu", "Ashanti", 26, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("fff440c0-3bee-dbc7-9b0e-bc59ca924a51"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Kasoa", "Greater Accra", 17, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "service_categories",
                columns: new[] { "id", "created_at", "default_pricing_mode", "description", "is_active", "name", "parent_category_id", "slug", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", "Homes and apartments: regular, deep and move-out cleans.", true, "Home cleaning", null, "cleaning", 1, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "StartingFrom", "Wash, iron and dry cleaning, with pickup and delivery.", true, "Laundry", null, "laundry", 2, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("06a99730-ddac-4236-f89c-16415419f50b"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, "Standard cleaning", new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-standard-cleaning", 1, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("07e5460b-7b17-0d2a-c4cb-2ee7b45703cb"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, "Move-in / move-out cleaning", new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-move-in-move-out-cleaning", 3, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("1fe15b16-391b-9998-e976-4508717cffb4"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, "After-party cleaning", new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-after-party-cleaning", 4, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("4029c0d7-d788-b6e7-79c2-c9a5b918eeb4"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "StartingFrom", null, true, "Pickup & delivery", new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-pickup-delivery", 6, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("820c71d2-56d7-ba96-901b-2aa702eed11c"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "StartingFrom", null, true, "Ironing only", new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-ironing-only", 3, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("8241cb02-b410-77c0-439d-191d2c7b2fa3"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, "Deep cleaning", new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-deep-cleaning", 2, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("ae9fbb78-2ed1-53d8-93af-5fbd590a6464"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "StartingFrom", null, true, "Dry cleaning", new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-dry-cleaning", 4, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b6068ad9-0f8f-d3e2-4c41-465f44690a33"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "StartingFrom", null, true, "Wash & fold", new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-wash-fold", 1, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("c907abd7-c286-fe7e-9538-28559f7e374a"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "StartingFrom", null, true, "Wash & iron", new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-wash-iron", 2, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("d6cea60e-84f1-78af-f95f-254b753873c7"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "StartingFrom", null, true, "Duvets & bulky items", new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-duvets-bulky-items", 5, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("dc8adb4b-39f4-6c93-64f2-8f36fd9ca3cf"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, "Sofa & carpet cleaning", new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-sofa-carpet-cleaning", 5, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("ea2d4d86-8d3a-448f-ae58-bc69df489c01"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, "Window cleaning", new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-window-cleaning", 6, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_objects_owner_consumer_id",
                table: "media_objects",
                column: "owner_consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_objects_storage_key",
                table: "media_objects",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_methods_one_default",
                table: "payout_methods",
                column: "provider_account_id",
                unique: true,
                filter: "is_default AND archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payout_methods_one_per_number",
                table: "payout_methods",
                columns: new[] { "provider_account_id", "account_number_fingerprint" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_provider_accounts_owner_consumer_id",
                table: "provider_accounts",
                column: "owner_consumer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_application_submissions_one_per_version",
                table: "provider_application_submissions",
                columns: new[] { "provider_application_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_applications_provider_account_id",
                table: "provider_applications",
                column: "provider_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_availability_rules_provider_account_id_day_of_week",
                table: "provider_availability_rules",
                columns: new[] { "provider_account_id", "day_of_week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_offerings_provider_account_id_service_category_id",
                table: "provider_offerings",
                columns: new[] { "provider_account_id", "service_category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_offerings_service_category_id",
                table: "provider_offerings",
                column: "service_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_portfolio_items_media_object_id",
                table: "provider_portfolio_items",
                column: "media_object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_portfolio_items_provider_account_id",
                table: "provider_portfolio_items",
                column: "provider_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_portfolio_items_service_category_id",
                table: "provider_portfolio_items",
                column: "service_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_service_areas_provider_account_id_service_area_id",
                table: "provider_service_areas",
                columns: new[] { "provider_account_id", "service_area_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_service_areas_service_area_id",
                table: "provider_service_areas",
                column: "service_area_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_standards_acceptances_accepted_by_consumer_id",
                table: "provider_standards_acceptances",
                column: "accepted_by_consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_standards_acceptances_one_per_version",
                table: "provider_standards_acceptances",
                columns: new[] { "provider_account_id", "standards_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_verification_documents_media_object_id",
                table: "provider_verification_documents",
                column: "media_object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_verification_documents_provider_account_id_documen",
                table: "provider_verification_documents",
                columns: new[] { "provider_account_id", "document_type" });

            migrationBuilder.CreateIndex(
                name: "ix_service_areas_region_name",
                table: "service_areas",
                columns: new[] { "region", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_categories_parent_category_id",
                table: "service_categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_categories_slug",
                table: "service_categories",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payout_methods");

            migrationBuilder.DropTable(
                name: "provider_application_submissions");

            migrationBuilder.DropTable(
                name: "provider_availability_rules");

            migrationBuilder.DropTable(
                name: "provider_offerings");

            migrationBuilder.DropTable(
                name: "provider_portfolio_items");

            migrationBuilder.DropTable(
                name: "provider_service_areas");

            migrationBuilder.DropTable(
                name: "provider_standards_acceptances");

            migrationBuilder.DropTable(
                name: "provider_verification_documents");

            migrationBuilder.DropTable(
                name: "provider_applications");

            migrationBuilder.DropTable(
                name: "service_categories");

            migrationBuilder.DropTable(
                name: "service_areas");

            migrationBuilder.DropTable(
                name: "media_objects");

            migrationBuilder.DropTable(
                name: "provider_accounts");
        }
    }
}
