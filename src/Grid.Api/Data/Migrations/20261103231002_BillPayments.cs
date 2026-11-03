using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grid.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BillPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ecg_kind",
                table: "utility_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "network",
                table: "utility_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "float_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_reference = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_float_transfers", x => x.id);
                    table.CheckConstraint("ck_float_transfers_amount", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    saved_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    account_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    destination = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bundle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    receipt_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    client_reference = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payer_phone = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    payment_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hubtel_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    hubtel_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    charges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    amount_charged = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    amount_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    payment_error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    needs_review = table.Column<bool>(type: "boolean", nullable: false),
                    review_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    idempotency_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_amounts", "principal > 0 AND fee >= 0 AND total = principal + fee");
                    table.ForeignKey(
                        name: "fk_orders_consumers_consumer_id",
                        column: x => x.consumer_id,
                        principalTable: "consumers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_reference = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    hubtel_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    commission = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bills", x => x.id);
                    table.CheckConstraint("ck_bills_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_bills_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_events_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    evidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_refunds_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bills_client_reference",
                table: "bills",
                column: "client_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bills_next_check_at",
                table: "bills",
                column: "next_check_at",
                filter: "next_check_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bills_order_id",
                table: "bills",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_float_transfers_client_reference",
                table: "float_transfers",
                column: "client_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_float_transfers_next_check_at",
                table: "float_transfers",
                column: "next_check_at",
                filter: "next_check_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_order_events_order_id_created_at",
                table: "order_events",
                columns: new[] { "order_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_client_reference",
                table: "orders",
                column: "client_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_consumer_id_created_at_id",
                table: "orders",
                columns: new[] { "consumer_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_consumer_id_idempotency_key_hash",
                table: "orders",
                columns: new[] { "consumer_id", "idempotency_key_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_next_check_at",
                table: "orders",
                column: "next_check_at",
                filter: "next_check_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_external_reference",
                table: "refunds",
                column: "external_reference",
                unique: true,
                filter: "external_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_next_check_at",
                table: "refunds",
                column: "next_check_at",
                filter: "next_check_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_order_id",
                table: "refunds",
                column: "order_id",
                unique: true);

            // History is append-only, even for bulk updates that bypass SaveChanges.
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_order_event_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'order_events is append-only'; END;
                $$;
                CREATE TRIGGER order_events_append_only BEFORE UPDATE OR DELETE ON order_events
                    FOR EACH ROW EXECUTE FUNCTION reject_order_event_change();
                """);

            // At most one float sweep in flight.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ix_float_transfers_one_open ON float_transfers ((true))
                    WHERE state IN ('Created', 'Submitting', 'Pending', 'Unknown');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bills");

            migrationBuilder.DropTable(
                name: "float_transfers");

            migrationBuilder.DropTable(
                name: "order_events");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropColumn(
                name: "ecg_kind",
                table: "utility_accounts");

            migrationBuilder.DropColumn(
                name: "network",
                table: "utility_accounts");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS reject_order_event_change();");
        }
    }
}
