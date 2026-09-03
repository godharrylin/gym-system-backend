using Dapper;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;

namespace gym_system.Infrastructures
{
    internal sealed class SqlOrderRepository : IOrderRepository
    {
        private readonly ISqlSession _session;

        public SqlOrderRepository(ISqlSession session)
        {
            _session = session;
        }

        public async Task<OrderPersistenceResult> AddAsync(Order order, CancellationToken ct)
        {
            var orderRow = await InsertOrderAsync(order, ct);
            var itemResults = new List<OrderItemPersistenceResult>(order.Items.Count);

            foreach (var item in order.Items)
            {
                var itemRow = await InsertOrderItemAsync(orderRow.orders_sn, order.OperatorId, item, ct);
                itemResults.Add(new OrderItemPersistenceResult
                {
                    ClientItemId = item.Id,
                    OrderItemSn = itemRow.order_items_sn,
                    OrderItemId = itemRow.order_items_id
                });
            }

            return new OrderPersistenceResult
            {
                OrderSn = orderRow.orders_sn,
                OrderId = orderRow.orders_id,
                Items = itemResults
            };
        }

        public async Task<UnpaidTicketOrder?> FindUnpaidTicketOrderAsync(
            string orderId,
            bool acquireLock,
            CancellationToken ct)
        {
            var lockHint = acquireLock ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
            var sql = $$"""
                SELECT
                    o.orders_sn AS OrderSn,
                    o.orders_id AS OrderId,
                    o.orders_buyer_id AS BuyerId,
                    i.order_items_sn AS OrderItemSn,
                    i.order_items_id AS OrderItemId,
                    i.order_items_ref_id AS TicketPlanKindCode,
                    i.order_items_quantity AS Quantity,
                    i.order_items_unit_price AS UnitPrice,
                    i.order_items_total_amount AS TotalAmount,
                    i.order_items_actual_amount AS ActualAmount
                FROM dbo.orders AS o{{lockHint}}
                INNER JOIN dbo.order_items AS i{{lockHint}}
                    ON i.orders_sn = o.orders_sn
                WHERE o.orders_id = @orderId
                  AND o.orders_overall_payment_state = 'UnPaid'
                  AND i.order_items_payment_state = 'UnPaid'
                  AND i.order_items_type = 'Ticket';
                """;

            var rows = (await _session.Connection.QueryAsync<UnpaidTicketOrder>(
                new CommandDefinition(
                    sql,
                    new { orderId },
                    transaction: _session.Transaction,
                    cancellationToken: ct))).AsList();

            return rows.Count switch
            {
                0 => null,
                1 => rows[0],
                _ => throw new InvalidOperationException("目前只支援單一票券明細的未付款訂單")
            };
        }

        public async Task MarkPaidAsync(
            int orderSn,
            int orderItemSn,
            DateTime paidAt,
            string paymentMethod,
            string operatorId,
            CancellationToken ct)
        {
            const string itemSql = """
                UPDATE dbo.order_items
                SET order_items_payment_state = 'Paid',
                    order_items_paid_at = @paidAt,
                    order_items_payment_method = @paymentMethod,
                    order_items_up_pn = @operatorId,
                    order_items_up_dt = @paidAt
                WHERE order_items_sn = @orderItemSn
                  AND orders_sn = @orderSn
                  AND order_items_payment_state = 'UnPaid'
                  AND order_items_paid_at IS NULL;
                """;
            var itemAffected = await _session.Connection.ExecuteAsync(
                new CommandDefinition(
                    itemSql,
                    new { orderSn, orderItemSn, paidAt, paymentMethod, operatorId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
            if (itemAffected != 1)
            {
                throw new InvalidOperationException("訂單明細付款狀態已變更，請重新整理");
            }

            const string orderSql = """
                UPDATE dbo.orders
                SET orders_overall_payment_state = 'Paid',
                    orders_up_pn = @operatorId,
                    orders_up_dt = @paidAt
                WHERE orders_sn = @orderSn
                  AND orders_overall_payment_state = 'UnPaid';
                """;
            var orderAffected = await _session.Connection.ExecuteAsync(
                new CommandDefinition(
                    orderSql,
                    new { orderSn, paidAt, operatorId },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
            if (orderAffected != 1)
            {
                throw new InvalidOperationException("訂單付款狀態已變更，請重新整理");
            }
        }

        private async Task<OrderRow> InsertOrderAsync(Order order, CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.orders
                (
                    order_buy_date,
                    orders_buyer_id,
                    orders_buyer_name,
                    orders_overall_payment_state,
                    orders_total_amount,
                    orders_actual_amount,
                    orders_create_pn,
                    orders_create_dt,
                    orders_up_pn,
                    orders_up_dt
                )
                OUTPUT INSERTED.orders_sn, INSERTED.orders_id
                VALUES
                (
                    @BuyAt,
                    @BuyerId,
                    @BuyerName,
                    @PaymentState,
                    @TotalAmount,
                    @ActualAmount,
                    @OperatorId,
                    @BuyAt,
                    @OperatorId,
                    @BuyAt
                );
                """;

            return await _session.Connection.QuerySingleAsync<OrderRow>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        order.BuyAt,
                        order.BuyerId,
                        order.BuyerName,
                        PaymentState = order.PaymentState.ToString(),
                        order.TotalAmount,
                        order.ActualAmount,
                        order.OperatorId
                    },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
        }

        private async Task<OrderItemRow> InsertOrderItemAsync(
            int orderSn,
            string operatorId,
            OrderItem item,
            CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.order_items
                (
                    orders_sn,
                    order_items_buy_date,
                    order_items_type,
                    order_items_ref_id,
                    order_items_name,
                    order_items_payment_state,
                    order_items_paid_at,
                    order_items_payment_method,
                    order_items_unit_price,
                    order_items_total_amount,
                    order_items_actual_amount,
                    order_items_quantity,
                    bonus_benefit_quantity,
                    bonus_benefit_unit,
                    order_items_create_pn,
                    order_items_create_dt,
                    discount_type,
                    discount_rate,
                    order_items_up_pn,
                    order_items_up_dt
                )
                OUTPUT INSERTED.order_items_sn, INSERTED.order_items_id
                VALUES
                (
                    @OrderSn,
                    @BuyAt,
                    @Type,
                    @RefId,
                    @Name,
                    @PaymentState,
                    @PaidAt,
                    @PaymentMethod,
                    @UnitPrice,
                    @TotalAmount,
                    @ActualAmount,
                    @Quantity,
                    @BonusQuantity,
                    @BonusUnit,
                    @OperatorId,
                    @BuyAt,
                    @DiscountType,
                    @DiscountRate,
                    @OperatorId,
                    @BuyAt
                );
                """;

            return await _session.Connection.QuerySingleAsync<OrderItemRow>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        OrderSn = orderSn,
                        item.BuyAt,
                        Type = item.Type.ToString(),
                        item.RefId,
                        item.Name,
                        PaymentState = item.PaymentState.ToString(),
                        item.PaidAt,
                        PaymentMethod = item.PaymentMethod.ToString(),
                        item.UnitPrice,
                        item.TotalAmount,
                        item.ActualAmount,
                        item.Quantity,
                        item.BonusQuantity,
                        BonusUnit = item.BonusUnit?.ToString(),
                        OperatorId = operatorId,
                        item.DiscountType,
                        item.DiscountRate
                    },
                    transaction: _session.Transaction,
                    cancellationToken: ct));
        }

        private sealed class OrderRow
        {
            public int orders_sn { get; init; }
            public string orders_id { get; init; } = string.Empty;
        }

        private sealed class OrderItemRow
        {
            public int order_items_sn { get; init; }
            public string order_items_id { get; init; } = string.Empty;
        }
    }
}



