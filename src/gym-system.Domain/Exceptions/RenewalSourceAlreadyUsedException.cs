namespace gym_system.Domain.Exceptions
{
    public sealed class RenewalSourceAlreadyUsedException : Exception
    {
        public RenewalSourceAlreadyUsedException()
            : base("此來源票券已經存在有效的續約票")
        {
        }
    }
}
