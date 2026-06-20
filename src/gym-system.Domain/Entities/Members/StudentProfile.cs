namespace gym_system.Domain.Entities.Members
{
    public sealed class StudentProfile
    {
        private StudentProfile(string userId)
        {
            UserId = userId;
        }

        public string UserId { get; }
        public DateTime? LastVisitAt { get; private set; }
        public CurrentTicketSnapshot? CurrentTicket { get; private set; }

        public static StudentProfile Create(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new InvalidOperationException("UserId 必填");
            }

            return new StudentProfile(userId.Trim());
        }

        public void UpdateCurrentTicket(CurrentTicketSnapshot snapshot)
        {
            CurrentTicket = snapshot;
        }
    }

    public sealed class CurrentTicketSnapshot
    {
        public string TicketId { get; init; } = string.Empty;
        public string TicketType { get; init; } = string.Empty;
        public string TicketValidState { get; init; } = string.Empty;
        public string TicketPaymentState { get; init; } = string.Empty;
        public int? TicketRemainCount { get; init; }
        public DateOnly? TicketExpireDate { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
