namespace gym_system.Application.Common
{
    public sealed class ForbiddenException : Exception
    {
        public ForbiddenException(string message) : base(message)
        {
        }
    }
}
