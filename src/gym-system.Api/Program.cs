using System.Text;
using gym_system.Application.DevicesUseCase;
using gym_system.Infrastructures.Devices;
using gym_system.Api.Authentication;
using gym_system.Api.Errors;
using gym_system.Application.AuthUseCase.LoginByPhone;
using gym_system.Application.AuthUseCase.RefreshAuthToken;
using gym_system.Application.AuthUseCase.Tokens;
using gym_system.Application.Common.Authorization;
using gym_system.Application.CoursesUseCase.Commands;
using gym_system.Application.CoursesUseCase.Queries;
using gym_system.Application.InstructorsUseCase.Command.CreateInstructor;
using gym_system.Application.InstructorsUseCase.Command.UpdateInstructor;
using gym_system.Application.InstructorsUseCase.Queries;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Application.OrdersUseCase.Commands.CreateOrder;
using gym_system.Application.OrdersUseCase.Commands.PayOrder;
using gym_system.Application.OrdersUseCase.Services;
using gym_system.Application.ScheduleRulesUseCase.Commands.CreateScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Commands.DeleteScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Commands.UpdateScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Queries;
using gym_system.Application.ScheduleSessionsUseCase.Command.CancelScheduleSession;
using gym_system.Application.ScheduleSessionsUseCase.Command.CreateScheduleSession;
using gym_system.Application.ScheduleSessionsUseCase.Command.GetOrEnsureScheduleWeek;
using gym_system.Application.ScheduleSessionsUseCase.Command.UpdateScheduleSession;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Application.TicketsUseCase.Commands.CancelQueuedRenewal;
using gym_system.Application.TicketsUseCase.Queries;
using gym_system.Infrastructures;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt 設定缺少");
if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) || Encoding.UTF8.GetByteCount(jwtSettings.SecretKey) < 32)
{
    throw new InvalidOperationException("Jwt:SecretKey 至少需要 32 bytes");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.FindFirst("token_type")?.Value != "access")
                {
                    context.Fail("Only access tokens can call protected APIs.");
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
var turnstileSettings = builder.Configuration.GetSection("Turnstile").Get<TurnstileSettings>() ?? new();
turnstileSettings.Validate();
builder.Services.AddSingleton(turnstileSettings);
builder.Services.AddSingleton<DeviceTestCommandGate>();
builder.Services.AddScoped<SendDeviceTestCommandHandler>();
builder.Services.AddHttpClient<ITurnstileCommandClient, CloudServerTurnstileClient>(client =>
{
    client.BaseAddress = new Uri(turnstileSettings.BaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
/* SQL Connection */
builder.Services.AddInfrastructureSql();
builder.Services.AddScoped<IAuthTokenGenerator, JwtAuthTokenGenerator>();
builder.Services.AddScoped<IRefreshTokenValidator, JwtRefreshTokenValidator>();
builder.Services.AddScoped<IUserPermissionService, UserPermissionService>();
builder.Services.AddScoped<LoginByPhoneHandler>();
builder.Services.AddScoped<RefreshAuthTokenHandler>();
builder.Services.AddScoped<CreateInstructorHandler>();
builder.Services.AddScoped<UpdateInstructorHandler>();
builder.Services.AddScoped<GetInstructorsListHandler>();
builder.Services.AddScoped<GetCoursesListHandler>();
builder.Services.AddScoped<UpdateCourseHandler>();
builder.Services.AddScoped<CreateCourseHandler>();
builder.Services.AddScoped<GetScheduleRulesQueryHandler>();
builder.Services.AddScoped<CreateScheduleRuleHandler>();
builder.Services.AddScoped<UpdateScheduleRuleHandler>();
builder.Services.AddScoped<DeleteScheduleRuleHandler>();
builder.Services.AddScoped<GetOrEnsureScheduleWeekHandler>();
builder.Services.AddScoped<UpdateScheduleSessionHandler>();
builder.Services.AddScoped<CreateScheduleSessionHandler>();
builder.Services.AddScoped<CancelScheduleSessionHandler>();
builder.Services.AddScoped<GetPurchasableTicketPlansForStudentHandler>();
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<PayOrderHandler>();
builder.Services.AddScoped<CancelQueuedRenewalHandler>();
builder.Services.AddScoped<GetStudentTicketPassesHandler>();
builder.Services.AddScoped<TicketPurchaseService>();
builder.Services.AddScoped<UnpaidTicketOrderPaymentService>();
builder.Services.AddScoped<RegisterMemberHandler>();
builder.Services.AddScoped<gym_system.Application.MembersUseCase.Queries.GetStudentMemberList.GetStudentMemberListHandler>();
builder.Services.AddScoped<gym_system.Application.MembersUseCase.Commands.UpdateStudent.UpdateStudentHandler>();
builder.Services.AddScoped<ITicketPlanEligibilityService, TicketPlanEligibilityService>();
builder.Services.AddScoped<RenewalTicketPassEligibilityService>();
builder.Services.AddScoped<ITicketPlanEligibilityRule, NewOnlyTicketPlanEligibilityRule>();
builder.Services.AddScoped<ITicketPlanEligibilityRule, RenewalTicketPlanEligibilityRule>();
/* Mock Connection */
//builder.Services.AddScoped<RegisterMemberHandler>();
//builder.Services.AddInfrastructureInMemory();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
