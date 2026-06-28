namespace v2rayF.Services;

public static class AppServices
{
    public static ICoreEnvironment CoreEnvironment { get; set; } = null!;

    public static IPlatformIntegration Platform { get; set; } = null!;
}
