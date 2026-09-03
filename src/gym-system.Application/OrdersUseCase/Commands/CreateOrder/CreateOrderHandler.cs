using gym_system.Application.OrdersUseCase.Services;
using gym_system.Domain.Repositories;

namespace gym_system.Application.OrdersUseCase.Commands.CreateOrder
{
    public sealed class CreateOrderHandler
    {
        private readonly TicketPurchaseService _ticketPurchaseService;
        private readonly IUnitOfWork _unitOfWork;

        public CreateOrderHandler(
            TicketPurchaseService ticketPurchaseService,
            IUnitOfWork unitOfWork)
        {
            _ticketPurchaseService = ticketPurchaseService;
            _unitOfWork = unitOfWork;
        }

        public async Task<CreateOrderResult> Handle(
            CreateOrderCommand command,
            CancellationToken ct = default)
        {
            await _unitOfWork.BeginAsync(ct);
            try
            {
                var purchase = await _ticketPurchaseService.PurchaseAsync(
                    new TicketPurchaseRequest
                    {
                        BuyerId = command.BuyerId,
                        BeneficiaryStudentIds = command.TicketPurchase.BeneficiaryStudentIds,
                        TicketPlanKindCode = command.TicketPurchase.TicketPlanKindCode,
                        Quantity = command.TicketPurchase.Quantity,
                        PaymentStatus = command.PaymentStatus,
                        PaymentMethod = command.PaymentMethod,
                        OperatorId = command.OperatorId
                    },
                    ct);

                await _unitOfWork.CommitAsync(ct);
                return new CreateOrderResult
                {
                    Success = true,
                    ErrorMessage = null,
                    OrderId = purchase.OrderId
                };
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
    }
}
