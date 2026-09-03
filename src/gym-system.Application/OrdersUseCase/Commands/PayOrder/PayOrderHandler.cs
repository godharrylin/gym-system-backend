using gym_system.Application.OrdersUseCase.Services;
using gym_system.Domain.Repositories;

namespace gym_system.Application.OrdersUseCase.Commands.PayOrder
{
    public sealed class PayOrderHandler
    {
        private readonly UnpaidTicketOrderPaymentService _paymentService;
        private readonly IUnitOfWork _unitOfWork;

        public PayOrderHandler(
            UnpaidTicketOrderPaymentService paymentService,
            IUnitOfWork unitOfWork)
        {
            _paymentService = paymentService;
            _unitOfWork = unitOfWork;
        }

        public async Task Handle(PayOrderCommand command, CancellationToken ct = default)
        {
            await _unitOfWork.BeginAsync(ct);
            try
            {
                await _paymentService.PayAsync(
                    command.OrderId,
                    command.PaymentMethod,
                    command.OperatorId,
                    ct);
                await _unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await _unitOfWork.RollbackAsync(ct);
                throw;
            }
        }
    }
}
