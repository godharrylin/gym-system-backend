using System.Text.Json.Serialization;

namespace gym_system.Api.Contracts
{
    public sealed class ApiErrorResponse
    {
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string TraceId { get; init; } = string.Empty;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ExceptionType { get; init; }
    }
}
