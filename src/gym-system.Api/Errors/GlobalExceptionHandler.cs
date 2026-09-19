using System.Diagnostics;
using gym_system.Api.Contracts;
using Microsoft.AspNetCore.Diagnostics;

namespace gym_system.Api.Errors
{
    public sealed class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IWebHostEnvironment _environment;

        public GlobalExceptionHandler(
            ILogger<GlobalExceptionHandler> logger,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
            _logger.LogError(
                exception,
                "Unhandled API exception. TraceId: {TraceId}",
                traceId);

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsJsonAsync(
                new ApiErrorResponse
                {
                    Code = "INTERNAL_SERVER_ERROR",
                    Message = "註冊失敗，請聯絡系統管理員",
                    TraceId = traceId,
                    ExceptionType = _environment.IsDevelopment()
                        ? exception.GetType().Name
                        : null
                },
                cancellationToken);

            return true;
        }
    }
}
