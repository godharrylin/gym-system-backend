namespace gym_system.Domain.Exceptions
{
    public sealed class MemberRegistrationRejectedException : InvalidOperationException
    {
        public MemberRegistrationRejectedException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public MemberRegistrationRejectedException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
