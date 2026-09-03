namespace gym_system.Domain.Exceptions
{
    public sealed class TicketPurchaseRejectedException : InvalidOperationException
    {
        public TicketPurchaseRejectedException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
