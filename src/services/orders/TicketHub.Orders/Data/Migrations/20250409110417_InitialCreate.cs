using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketHub.Orders.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cancellation_sagas",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cancellation_sagas", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "orders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BuyerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HoldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Subtotal = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                Fees = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                Total = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                PaymentProvider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                PaymentRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ClientSecret = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                StatusReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RefundedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_orders", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Topic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                TraceParent = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Attempts = table.Column<int>(type: "int", nullable: false),
                LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payment_webhook_events",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_webhook_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "processed_events",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Consumer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_processed_events", x => new { x.EventId, x.Consumer });
            });

        migrationBuilder.CreateTable(
            name: "cancellation_saga_steps",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SagaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Attempts = table.Column<int>(type: "int", nullable: false),
                LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cancellation_saga_steps", x => x.Id);
                table.ForeignKey(
                    name: "FK_cancellation_saga_steps_cancellation_sagas_SagaId",
                    column: x => x.SagaId,
                    principalTable: "cancellation_sagas",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "order_lines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SeatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Price = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_order_lines_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "tickets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SeatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CheckedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CheckedInBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tickets", x => x.Id);
                table.ForeignKey(
                    name: "FK_tickets_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_cancellation_saga_steps_SagaId_OrderId",
            table: "cancellation_saga_steps",
            columns: new[] { "SagaId", "OrderId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_cancellation_sagas_EventId",
            table: "cancellation_sagas",
            column: "EventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_order_lines_OrderId",
            table: "order_lines",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_orders_BuyerId_IdempotencyKey",
            table: "orders",
            columns: new[] { "BuyerId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_orders_EventId_Status",
            table: "orders",
            columns: new[] { "EventId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_orders_HoldId",
            table: "orders",
            column: "HoldId");

        migrationBuilder.CreateIndex(
            name: "IX_orders_PaymentRef",
            table: "orders",
            column: "PaymentRef");

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedAt_OccurredAt",
            table: "outbox_messages",
            columns: new[] { "ProcessedAt", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_tickets_BuyerId",
            table: "tickets",
            column: "BuyerId");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_Code",
            table: "tickets",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tickets_EventId_SeatId",
            table: "tickets",
            columns: new[] { "EventId", "SeatId" });

        migrationBuilder.CreateIndex(
            name: "IX_tickets_OrderId",
            table: "tickets",
            column: "OrderId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cancellation_saga_steps");

        migrationBuilder.DropTable(
            name: "order_lines");

        migrationBuilder.DropTable(
            name: "outbox_messages");

        migrationBuilder.DropTable(
            name: "payment_webhook_events");

        migrationBuilder.DropTable(
            name: "processed_events");

        migrationBuilder.DropTable(
            name: "tickets");

        migrationBuilder.DropTable(
            name: "cancellation_sagas");

        migrationBuilder.DropTable(
            name: "orders");
    }
}
