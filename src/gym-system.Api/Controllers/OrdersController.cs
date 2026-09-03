using gym_system.Api.Contracts.Orders;
using gym_system.Application.OrdersUseCase.Commands.CreateOrder;
using gym_system.Application.OrdersUseCase.Commands.PayOrder;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/orders")]
    public sealed class OrdersController : ControllerBase
    {
        private readonly CreateOrderHandler _handler;
        private readonly PayOrderHandler _payHandler;

        public OrdersController(CreateOrderHandler handler, PayOrderHandler payHandler)
        {
            _handler = handler;
            _payHandler = payHandler;
        }

        [HttpPost("{orderId}/pay")]
        public async Task<IActionResult> PayAsync(
            [FromRoute] string orderId,
            [FromBody] PayOrderRequest request,
            CancellationToken ct)
        {
            try
            {
                await _payHandler.Handle(
                    new PayOrderCommand
                    {
                        OrderId = orderId,
                        PaymentMethod = request.PaymentMethod,
                        OperatorId = "ADMIN_PLACEHOLDER"
                    },
                    ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { code = "TICKET_PLAN_NOT_AVAILABLE", message = ex.Message });
            }
            catch (RenewalSourceAlreadyUsedException ex)
            {
                return Conflict(new { code = "RENEWAL_SOURCE_ALREADY_USED", message = ex.Message });
            }
            catch (TicketPurchaseRejectedException ex)
            {
                return Conflict(new { code = ex.Code, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "ORDER_PAYMENT_REJECTED", message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<CreateOrderResponse>> CreateAsync(
            [FromBody] CreateOrderRequest request,
            CancellationToken ct)
        {
            try
            {
                var command = new CreateOrderCommand
                {
                    BuyerId = request.BuyerId,
                    PaymentStatus = ParsePaymentStatus(request.PaymentStatus),
                    PaymentMethod = request.PaymentMethod,
                    TicketPurchase = new TicketPurchaseOrderInput
                    {
                        TicketPlanKindCode = request.TicketPurchase.TicketPlanKindCode,
                        BeneficiaryStudentIds = request.TicketPurchase.BeneficiaryStudentIds,
                        Quantity = request.TicketPurchase.Quantity
                    },
                    OperatorId = "ADMIN_PLACEHOLDER"
                };

                var result = await _handler.Handle(command, ct);
                return Ok(new CreateOrderResponse
                {
                    Success = result.Success,
                    ErrorCode = null,
                    ErrorMessage = result.ErrorMessage,
                    OrderId = result.OrderId
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(Fail("TICKET_PLAN_NOT_AVAILABLE", ex.Message));
            }
            catch (TicketPurchaseRejectedException ex)
            {
                return Conflict(Fail(ex.Code, ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(Fail("INVALID_TICKET_PURCHASE", ex.Message));
            }
            catch (RenewalSourceAlreadyUsedException ex)
            {
                return Conflict(Fail("RENEWAL_SOURCE_ALREADY_USED", ex.Message));
            }
            catch
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Fail("TICKET_PURCHASE_FAILED", "購買建立失敗，請稍後再試"));
            }
        }

        private static PaymentState ParsePaymentStatus(string status)
        {
            if (status.Equals("PAID", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Paid", StringComparison.OrdinalIgnoreCase))
            {
                return PaymentState.Paid;
            }

            if (status.Equals("UNPAID", StringComparison.OrdinalIgnoreCase)
                || status.Equals("UnPaid", StringComparison.OrdinalIgnoreCase))
            {
                return PaymentState.UnPaid;
            }

            throw new InvalidOperationException("付款狀態不支援");
        }

        private static CreateOrderResponse Fail(string code, string message)
        {
            return new CreateOrderResponse
            {
                Success = false,
                ErrorCode = code,
                ErrorMessage = message,
                OrderId = null
            };
        }
    }
}

