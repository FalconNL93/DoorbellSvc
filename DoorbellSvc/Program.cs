namespace DoorbellSvc;

internal static class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Starting Doorbell Service...");
        var svc = new DoorbellService();

        try
        {
            svc.Run();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Unable to start Doorbell Service: {e.Message}" +
                                    $"{Environment.NewLine}{e}");
            throw;
        }
    }
}
