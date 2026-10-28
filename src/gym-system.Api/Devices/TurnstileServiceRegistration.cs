using gym_system.Application.DevicesUseCase;
using gym_system.Infrastructures.Devices;

namespace gym_system.Api.Devices;

public static class TurnstileServiceRegistration
{
    public static IServiceCollection AddTurnstileControl(this IServiceCollection services,
        TurnstileSettings settings, bool isProduction)
    {
        settings.Validate(isProduction);
        services.AddSingleton(settings);
        services.AddSingleton<DeviceTestCommandGate>();
        services.AddScoped<SendDeviceTestCommandHandler>();
        services.AddScoped<GetDeviceTestStatusHandler>();
        services.AddScoped<gym_system.Application.GymAccessUseCase.GatePassageService>();
        if (settings.Mode == "Mock")
            services.AddTransient<ITurnstileCommandClient, MockTurnstileCommandClient>();
        else
            services.AddHttpClient<ITurnstileCommandClient, CloudServerTurnstileClient>(client =>
            {
                client.BaseAddress = new Uri(settings.BaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
