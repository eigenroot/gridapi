using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Grid.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ServicePackagesAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key_hash",
                table: "service_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "preferred_provider_account_id",
                table: "service_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "service_requests",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "booking_mode",
                table: "service_categories",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_launched",
                table: "service_categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "section",
                table: "service_categories",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_reviews_consumers_customer_consumer_id",
                        column: x => x.customer_consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_provider_accounts_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "provider_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    group = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    includes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    excludes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    unit_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    max_quantity = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_packages_service_categories_service_category_id",
                        column: x => x.service_category_id,
                        principalTable: "service_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payer_phone = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    client_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    hubtel_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    hubtel_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    charges = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    amount_paid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    refund_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    refund_reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_payments_consumers_consumer_id",
                        column: x => x.consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_payments_service_requests_service_request_id",
                        column: x => x.service_request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_request_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(170)", maxLength: 170, nullable: false),
                    unit_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_request_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_request_lines_service_packages_service_package_id",
                        column: x => x.service_package_id,
                        principalTable: "service_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_request_lines_service_requests_service_request_id",
                        column: x => x.service_request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("06a99730-ddac-4236-f89c-16415419f50b"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("07e5460b-7b17-0d2a-c4cb-2ee7b45703cb"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("1fe15b16-391b-9998-e976-4508717cffb4"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("4029c0d7-d788-b6e7-79c2-c9a5b918eeb4"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("820c71d2-56d7-ba96-901b-2aa702eed11c"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("8241cb02-b410-77c0-439d-191d2c7b2fa3"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("ae9fbb78-2ed1-53d8-93af-5fbd590a6464"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("b6068ad9-0f8f-d3e2-4c41-465f44690a33"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("c907abd7-c286-fe7e-9538-28559f7e374a"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("d6cea60e-84f1-78af-f95f-254b753873c7"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("dc8adb4b-39f4-6c93-64f2-8f36fd9ca3cf"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.UpdateData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("ea2d4d86-8d3a-448f-ae58-bc69df489c01"),
                columns: new[] { "booking_mode", "is_launched", "section" },
                values: new object[] { "FixedPrice", true, "Cleaning & laundry" });

            migrationBuilder.InsertData(
                table: "service_categories",
                columns: new[] { "id", "booking_mode", "created_at", "default_pricing_mode", "description", "is_active", "is_launched", "name", "parent_category_id", "section", "slug", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { new Guid("691caa33-a599-5942-5fae-7719499145e3"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", "Split and window units: servicing, deep cleans, gas and repairs.", true, true, "AC servicing", null, "Repairs & maintenance", "ac-servicing", 3, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("8c469b4e-e1ae-99b0-da22-aacbabbd1c85"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", "Sockets, switches, lighting, breakers and wiring faults.", true, false, "Electrical", null, "Repairs & maintenance", "electrical", 6, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "FixedPrice", "Fumigation for mosquitoes, cockroaches, rodents and termites.", true, true, "Pest control", null, "Repairs & maintenance", "pest-control", 4, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", "Leaks, blocked drains, taps, toilets, water heaters and pumps.", true, false, "Plumbing", null, "Repairs & maintenance", "plumbing", 5, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "service_packages",
                columns: new[] { "id", "created_at", "duration_minutes", "excludes", "group", "includes", "is_active", "max_quantity", "name", "price", "service_category_id", "slug", "sort_order", "summary", "unit_label", "updated_at" },
                values: new object[,]
                {
                    { new Guid("0ca0d5e7-5b51-3610-7209-f593c5d08ecb"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 180, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Standard clean", "Dusting and wiping of all surfaces\nFloors swept and mopped\nKitchen counters, sink and hob\nBathrooms and toilets\nBins emptied", true, 1, "2 bedrooms", 250m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-standard-2-bed", 2, "Keeps a home in shape. Supplies included.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("23f2ff2b-366d-990c-4cf4-2108fab6108d"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 120, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Standard clean", "Dusting and wiping of all surfaces\nFloors swept and mopped\nKitchen counters, sink and hob\nBathrooms and toilets\nBins emptied", true, 1, "1 bedroom", 180m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-standard-1-bed", 1, "Keeps a home in shape. Supplies included.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("327175a8-bd3f-0518-3147-14e101955014"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1440, "Ironing\nDry-clean-only items", "Wash & fold", "Collection and return\nWashing and drying\nFolding", true, 10, "Wash & fold", 60m, new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-wash-fold", 14, "Washed, dried and folded. Collected and returned within 48 hours.", "bag", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("3c4fe05e-1a3a-785e-f6b3-c296c4fe8707"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 30, "Leather restoration", "Sofa cleaning", "Vacuuming\nStain treatment\nShampoo and extraction", true, 12, "Sofa cleaning", 60m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-sofa-seat", 13, "Shampoo and extraction for fabric sofas, priced per seat.", "seat", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("4a6d7e5e-7c67-1a2e-1737-d335bd0250b6"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1440, "Dry-clean-only items", "Wash & iron", "Collection and return\nWashing and drying\nIroning\nShirts on hangers", true, 10, "Wash & iron", 90m, new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-wash-iron", 15, "Washed, dried and ironed. Shirts on hangers.", "bag", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("4aead8ac-2cf1-08c8-25aa-23d90090145c"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 480, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Move-in / move-out", "Everything in a deep clean\nEmpty-home walls spot-cleaned\nInside wardrobes and drawers\nBalconies and storerooms", true, 1, "4 bedrooms", 840m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-move-4-bed", 12, "For an empty home, before you move in or after you move out.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("5e9ed7df-7e98-01b2-f700-c2209ef88a6e"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 300, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Standard clean", "Dusting and wiping of all surfaces\nFloors swept and mopped\nKitchen counters, sink and hob\nBathrooms and toilets\nBins emptied", true, 1, "4 bedrooms", 420m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-standard-4-bed", 4, "Keeps a home in shape. Supplies included.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("62bdf311-3721-6c32-7274-1b5d9670ac51"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 420, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Move-in / move-out", "Everything in a deep clean\nEmpty-home walls spot-cleaned\nInside wardrobes and drawers\nBalconies and storerooms", true, 1, "3 bedrooms", 660m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-move-3-bed", 11, "For an empty home, before you move in or after you move out.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("6323018b-fcd3-8267-a534-102cf86c886f"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 240, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Standard clean", "Dusting and wiping of all surfaces\nFloors swept and mopped\nKitchen counters, sink and hob\nBathrooms and toilets\nBins emptied", true, 1, "3 bedrooms", 330m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-standard-3-bed", 3, "Keeps a home in shape. Supplies included.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("70e9f150-9f16-4688-5382-6876cba1f208"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1440, null, "Duvets & bulky items", "Collection and return\nWashing and drying", true, 10, "Duvets & bulky items", 50m, new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-duvet", 16, "Duvets, blankets and curtains.", "item", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("7ed230d8-4a01-d0ef-4106-a45e3bea0075"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 2880, null, "Dry cleaning", "Collection and return\nDry cleaning\nPressing", true, 20, "Dry cleaning", 70m, new Guid("763283de-87d1-935a-b54a-b0e06b1cbf67"), "laundry-dry-clean-suit", 17, "Suits, kaftans, dresses and anything marked dry clean.", "garment", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("855daaa3-3958-ebac-231a-0d09c4d9f830"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 240, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Deep clean", "Everything in a standard clean\nInside cupboards and appliances\nTiles, grout and fittings scrubbed\nDoors, frames and switches\nWindows from inside", true, 1, "1 bedroom", 320m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-deep-1-bed", 5, "Top to bottom, inside and out. Two cleaners for larger homes.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("858c8005-3270-2e13-d78b-fa92b1e18292"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 360, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Deep clean", "Everything in a standard clean\nInside cupboards and appliances\nTiles, grout and fittings scrubbed\nDoors, frames and switches\nWindows from inside", true, 1, "3 bedrooms", 590m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-deep-3-bed", 7, "Top to bottom, inside and out. Two cleaners for larger homes.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b260f7e6-c047-fb16-0f43-13a605a72bee"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 300, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Deep clean", "Everything in a standard clean\nInside cupboards and appliances\nTiles, grout and fittings scrubbed\nDoors, frames and switches\nWindows from inside", true, 1, "2 bedrooms", 450m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-deep-2-bed", 6, "Top to bottom, inside and out. Two cleaners for larger homes.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b3beb97a-ffa2-8e98-9bea-529b91030bc2"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 360, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Move-in / move-out", "Everything in a deep clean\nEmpty-home walls spot-cleaned\nInside wardrobes and drawers\nBalconies and storerooms", true, 1, "2 bedrooms", 500m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-move-2-bed", 10, "For an empty home, before you move in or after you move out.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("cec65565-6c19-89ff-b0f0-cfa148a35ebd"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 300, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Move-in / move-out", "Everything in a deep clean\nEmpty-home walls spot-cleaned\nInside wardrobes and drawers\nBalconies and storerooms", true, 1, "1 bedroom", 360m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-move-1-bed", 9, "For an empty home, before you move in or after you move out.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("e69def1b-9f4a-1d5b-dfc6-edc916779b29"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 420, "Laundry and ironing\nOutside windows above ground floor\nMoving heavy furniture", "Deep clean", "Everything in a standard clean\nInside cupboards and appliances\nTiles, grout and fittings scrubbed\nDoors, frames and switches\nWindows from inside", true, 1, "4 bedrooms", 760m, new Guid("5c0b5d49-e48f-3928-7b11-42c48b97a81d"), "cleaning-deep-4-bed", 8, "Top to bottom, inside and out. Two cleaners for larger homes.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "service_categories",
                columns: new[] { "id", "booking_mode", "created_at", "default_pricing_mode", "description", "is_active", "is_launched", "name", "parent_category_id", "section", "slug", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { new Guid("07c2c452-3978-d94b-bcf1-dcade7069f6e"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, true, "Fault diagnosis", new Guid("691caa33-a599-5942-5fae-7719499145e3"), "Repairs & maintenance", "ac-servicing-fault-diagnosis", 4, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("1d64c0c2-69b8-9216-187f-943b0abc11e2"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Appliance connection", new Guid("8c469b4e-e1ae-99b0-da22-aacbabbd1c85"), "Repairs & maintenance", "electrical-appliance-connection", 5, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("25614b4b-1255-f17a-94dc-840d22c3e678"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Toilets", new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"), "Repairs & maintenance", "plumbing-toilets", 3, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("2c408892-b9dc-6723-9f2d-2615aee5a287"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, true, "Installation", new Guid("691caa33-a599-5942-5fae-7719499145e3"), "Repairs & maintenance", "ac-servicing-installation", 5, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("2cdf039e-b719-2c3a-e36e-a1bdd0260927"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, true, "Gas top-up", new Guid("691caa33-a599-5942-5fae-7719499145e3"), "Repairs & maintenance", "ac-servicing-gas-top-up", 3, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("318cc274-c140-111b-eedc-38ddefb1db1b"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Tanks & pumps", new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"), "Repairs & maintenance", "plumbing-tanks-pumps", 6, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("3513d24e-6cff-18c7-0b50-e107a5e64d7c"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Sockets & switches", new Guid("8c469b4e-e1ae-99b0-da22-aacbabbd1c85"), "Repairs & maintenance", "electrical-sockets-switches", 1, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("483351a3-d247-6ff9-dc46-92e11db06448"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "FixedPrice", null, true, true, "General fumigation", new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "Repairs & maintenance", "pest-control-general-fumigation", 1, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("52b5557f-2b93-990e-97b7-aca4a45ed7f5"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, true, "General servicing", new Guid("691caa33-a599-5942-5fae-7719499145e3"), "Repairs & maintenance", "ac-servicing-general-servicing", 1, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("67cd70c1-55d7-4a9b-b46c-624139f8c57d"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Breakers & fuses", new Guid("8c469b4e-e1ae-99b0-da22-aacbabbd1c85"), "Repairs & maintenance", "electrical-breakers-fuses", 3, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("84027c21-80a4-8b25-9b3d-f63a7a9bb2fd"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Taps & showers", new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"), "Repairs & maintenance", "plumbing-taps-showers", 4, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("843b16b5-9090-8710-5db4-328265a0c740"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Lighting", new Guid("8c469b4e-e1ae-99b0-da22-aacbabbd1c85"), "Repairs & maintenance", "electrical-lighting", 2, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a6dd6a38-c797-ee00-0da2-006a968452f5"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PerUnit", null, true, true, "Deep foam cleaning", new Guid("691caa33-a599-5942-5fae-7719499145e3"), "Repairs & maintenance", "ac-servicing-deep-foam-cleaning", 2, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("aa119af9-6cc2-2019-1cb7-cd1bd5e5147c"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Leaks", new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"), "Repairs & maintenance", "plumbing-leaks", 1, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("aebdcf38-a9ff-cfc0-641b-736652462f13"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Wiring faults", new Guid("8c469b4e-e1ae-99b0-da22-aacbabbd1c85"), "Repairs & maintenance", "electrical-wiring-faults", 4, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b756d7a5-ef1a-d962-6d63-285d4d9effa6"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "FixedPrice", null, true, true, "Rodent control", new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "Repairs & maintenance", "pest-control-rodent-control", 2, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("ba9fedef-c03f-efef-ecf6-0c8e7ec8fa82"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "FixedPrice", null, true, true, "Termite treatment", new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "Repairs & maintenance", "pest-control-termite-treatment", 3, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("c8ffc721-afa4-5c18-6ebf-91391c71a69d"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Water heaters", new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"), "Repairs & maintenance", "plumbing-water-heaters", 5, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("d7a25672-9b2e-d948-3931-56b653e96407"), "FixedPrice", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "FixedPrice", null, true, true, "Bed bugs", new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "Repairs & maintenance", "pest-control-bed-bugs", 4, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("d8a5b90b-0567-c997-bdb5-6afcec0cf4e0"), "CallOutThenQuote", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "InspectionThenQuote", null, true, false, "Blocked drains", new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"), "Repairs & maintenance", "plumbing-blocked-drains", 2, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "service_packages",
                columns: new[] { "id", "created_at", "duration_minutes", "excludes", "group", "includes", "is_active", "max_quantity", "name", "price", "service_category_id", "slug", "sort_order", "summary", "unit_label", "updated_at" },
                values: new object[,]
                {
                    { new Guid("0438d778-d84a-aab6-8445-bf248ce31aaa"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 210, "Termites\nRodents", "General fumigation", "Indoor spraying\nCompound perimeter spray\nGel bait in the kitchen\nRe-treatment within 30 days if pests return", true, 1, "5 bedrooms or more", 750m, new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "pest-control-fumigation-xl", 24, "Mosquitoes, cockroaches, ants and flies. Licensed operators and approved products.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("22bf740c-0f42-ef2a-5713-8d9ce6efe272"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 90, "Gas top-up\nSpare parts", "Deep foam clean", "Everything in a general service\nFoam-jet clean of the indoor unit\nBlower and fan cleaned", true, 6, "Deep foam clean", 350m, new Guid("691caa33-a599-5942-5fae-7719499145e3"), "ac-servicing-deep-foam", 19, "For units that smell, drip or haven't been cleaned in a year.", "unit", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("504e0c84-e99e-d954-2eb7-9d2a710c459b"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 60, "Gas top-up\nSpare parts", "General service", "Filter wash\nIndoor and outdoor coil cleaning\nDrain line flush\nGas pressure and leak check", true, 6, "General service", 250m, new Guid("691caa33-a599-5942-5fae-7719499145e3"), "ac-servicing-general-service", 18, "Filters, coils and drain cleaned; gas pressure checked.", "unit", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("64631f94-21a1-a4bd-e0dc-79f240e3d884"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 60, "Repairs and parts, until you approve the quote", "Call-out & diagnosis", "Visit and diagnosis\nA quote in the app", true, 1, "Call-out & diagnosis", 120m, new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"), "plumbing-call-out", 27, "A plumber comes, finds the problem and quotes the fix. You approve the price before any work.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("68494698-cc46-a9f5-6c3e-9d5f2027e105"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 150, "Termites\nRodents", "General fumigation", "Indoor spraying\nCompound perimeter spray\nGel bait in the kitchen\nRe-treatment within 30 days if pests return", true, 1, "3 to 4 bedrooms", 550m, new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "pest-control-fumigation-large", 23, "Mosquitoes, cockroaches, ants and flies. Licensed operators and approved products.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("780cc04c-f7b7-db68-e33e-9fa13f386711"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 60, "Leak repair\nSpare parts", "Gas top-up", "Leak check\nRefrigerant top-up\nCooling test", true, 6, "Gas top-up", 450m, new Guid("691caa33-a599-5942-5fae-7719499145e3"), "ac-servicing-gas-top-up", 20, "Refrigerant topped up after a leak check.", "unit", new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("784ac57b-e099-0953-4315-48b1545740f4"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 60, "The treatment itself", "Termite inspection", "Inspection\nA written treatment quote", true, 1, "Termite inspection", 200m, new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "pest-control-termite-inspection", 26, "Finds the extent of the damage and quotes the treatment.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("7e47d528-4f33-c8d6-fa3a-f561b47eb2e9"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 90, "Termites\nRodents", "General fumigation", "Indoor spraying\nCompound perimeter spray\nGel bait in the kitchen\nRe-treatment within 30 days if pests return", true, 1, "Studio to 2 bedrooms", 400m, new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "pest-control-fumigation-small", 22, "Mosquitoes, cockroaches, ants and flies. Licensed operators and approved products.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("95e598b6-09c5-0358-2e8a-3ce5c56504b8"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 45, "The repair itself\nSpare parts", "Not cooling", "Fault diagnosis\nA written quote for the fix", true, 1, "Diagnosis", 150m, new Guid("691caa33-a599-5942-5fae-7719499145e3"), "ac-servicing-not-cooling", 21, "A technician finds the fault and tells you the fix and price before doing it.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("c1ca0dd4-837c-616b-5408-ae059835afe3"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 60, "Repairs and parts, until you approve the quote", "Call-out & diagnosis", "Visit and diagnosis\nA quote in the app", true, 1, "Call-out & diagnosis", 150m, new Guid("8c469b4e-e1ae-99b0-da22-aacbabbd1c85"), "electrical-call-out", 28, "A certified electrician finds the fault and quotes the fix. You approve the price before any work.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("d62dd8d5-51bd-469b-c892-85c14873f8de"), new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 90, null, "Rodent control", "Inspection\nBait stations and traps\nOne follow-up visit", true, 1, "Rodent control", 450m, new Guid("dca3687a-fe44-d130-7519-37be218a2314"), "pest-control-rodents", 25, "Baiting and traps, with a follow-up visit.", null, new DateTimeOffset(new DateTime(2026, 10, 17, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_one_per_key",
                table: "service_requests",
                columns: new[] { "customer_consumer_id", "idempotency_key_hash" },
                unique: true,
                filter: "idempotency_key_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_preferred_provider_account_id",
                table: "service_requests",
                column: "preferred_provider_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_customer_consumer_id",
                table: "reviews",
                column: "customer_consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_job_id",
                table: "reviews",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_provider_account_id",
                table: "reviews",
                column: "provider_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_packages_service_category_id_sort_order",
                table: "service_packages",
                columns: new[] { "service_category_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_service_packages_slug",
                table: "service_packages",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_payments_client_reference",
                table: "service_payments",
                column: "client_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_payments_consumer_id",
                table: "service_payments",
                column: "consumer_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_payments_next_check_at",
                table: "service_payments",
                column: "next_check_at",
                filter: "next_check_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_service_payments_one_paid_per_request",
                table: "service_payments",
                column: "service_request_id",
                unique: true,
                filter: "state = 'Succeeded'");

            migrationBuilder.CreateIndex(
                name: "ix_service_request_lines_service_package_id",
                table: "service_request_lines",
                column: "service_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_request_lines_service_request_id_sort_order",
                table: "service_request_lines",
                columns: new[] { "service_request_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_service_requests_provider_accounts_preferred_provider_accou",
                table: "service_requests",
                column: "preferred_provider_account_id",
                principalTable: "provider_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_service_requests_provider_accounts_preferred_provider_accou",
                table: "service_requests");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "service_payments");

            migrationBuilder.DropTable(
                name: "service_request_lines");

            migrationBuilder.DropTable(
                name: "service_packages");

            migrationBuilder.DropIndex(
                name: "ix_service_requests_one_per_key",
                table: "service_requests");

            migrationBuilder.DropIndex(
                name: "ix_service_requests_preferred_provider_account_id",
                table: "service_requests");

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("07c2c452-3978-d94b-bcf1-dcade7069f6e"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("1d64c0c2-69b8-9216-187f-943b0abc11e2"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("25614b4b-1255-f17a-94dc-840d22c3e678"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("2c408892-b9dc-6723-9f2d-2615aee5a287"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("2cdf039e-b719-2c3a-e36e-a1bdd0260927"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("318cc274-c140-111b-eedc-38ddefb1db1b"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("3513d24e-6cff-18c7-0b50-e107a5e64d7c"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("483351a3-d247-6ff9-dc46-92e11db06448"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("52b5557f-2b93-990e-97b7-aca4a45ed7f5"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("67cd70c1-55d7-4a9b-b46c-624139f8c57d"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("84027c21-80a4-8b25-9b3d-f63a7a9bb2fd"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("843b16b5-9090-8710-5db4-328265a0c740"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("a6dd6a38-c797-ee00-0da2-006a968452f5"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("aa119af9-6cc2-2019-1cb7-cd1bd5e5147c"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("aebdcf38-a9ff-cfc0-641b-736652462f13"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("b756d7a5-ef1a-d962-6d63-285d4d9effa6"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("ba9fedef-c03f-efef-ecf6-0c8e7ec8fa82"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("c8ffc721-afa4-5c18-6ebf-91391c71a69d"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("d7a25672-9b2e-d948-3931-56b653e96407"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("d8a5b90b-0567-c997-bdb5-6afcec0cf4e0"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("691caa33-a599-5942-5fae-7719499145e3"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("8c469b4e-e1ae-99b0-da22-aacbabbd1c85"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("dca3687a-fe44-d130-7519-37be218a2314"));

            migrationBuilder.DeleteData(
                table: "service_categories",
                keyColumn: "id",
                keyValue: new Guid("e50c2ba7-4a8f-e2b3-af42-18ead44c71ff"));

            migrationBuilder.DropColumn(
                name: "idempotency_key_hash",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "preferred_provider_account_id",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "total",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "booking_mode",
                table: "service_categories");

            migrationBuilder.DropColumn(
                name: "is_launched",
                table: "service_categories");

            migrationBuilder.DropColumn(
                name: "section",
                table: "service_categories");
        }
    }
}
