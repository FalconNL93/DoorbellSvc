using DoorbellSvc.Core;

namespace DoorbellSvc;

/// <summary>
/// Entry point for the doorbell service application
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("This service can only run on Linux.");
            Environment.Exit(1);
        }

        Console.WriteLine("Starting Doorbell Service...");

        using var service = new DoorbellServiceHost();

        try
        {
            service.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Service error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
