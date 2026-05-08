using gym_system.Application.InstructorsUseCase.Command.CreateInstructor;
using gym_system.Application.InstructorsUseCase.Command.UpdateInstructor;
using gym_system.Application.InstructorsUseCase.Queries;
using gym_system.Application.ClassesUseCase.Queries;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Infrastructures;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
/* SQL Connection */
builder.Services.AddInfrastructureSql();
builder.Services.AddScoped<CreateInstructorHandler>();
builder.Services.AddScoped<UpdateInstructorHandler>();
builder.Services.AddScoped<GetInstructorsListHandler>();
builder.Services.AddScoped<GetClassesListHandler>();
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

