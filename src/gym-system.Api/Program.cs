using gym_system.Application.CoursesUseCase.Commands;
using gym_system.Application.CoursesUseCase.Queries;
using gym_system.Application.InstructorsUseCase.Command.CreateInstructor;
using gym_system.Application.InstructorsUseCase.Command.UpdateInstructor;
using gym_system.Application.InstructorsUseCase.Queries;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Application.ScheduleRulesUseCase.Commands.CreateScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Commands.DeleteScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Commands.UpdateScheduleRule;
using gym_system.Application.ScheduleRulesUseCase.Queries;
using gym_system.Application.ScheduleSessionsUseCase.Command.CancelScheduleSession;
using gym_system.Application.ScheduleSessionsUseCase.Command.CreateScheduleSession;
using gym_system.Application.ScheduleSessionsUseCase.Command.GetOrEnsureScheduleWeek;
using gym_system.Application.ScheduleSessionsUseCase.Command.UpdateScheduleSession;
using gym_system.Infrastructures;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
/* SQL Connection */
builder.Services.AddInfrastructureSql();
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
/* Mock Connection */
//builder.Services.AddScoped<RegisterMemberHandler>();
//builder.Services.AddInfrastructureInMemory();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

