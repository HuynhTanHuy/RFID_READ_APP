namespace CareHR.RfidGateway.Utils;

public static class LogExport
{
    public static string? FindLatestLogFile(string logDirectory)
    {
        if (!Directory.Exists(logDirectory))
        {
            return null;
        }

        return Directory.GetFiles(logDirectory, "gateway-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static void ExportLatest(string logDirectory, string destinationPath)
    {
        var latest = FindLatestLogFile(logDirectory)
                     ?? throw new FileNotFoundException("No log file found.", logDirectory);

        File.Copy(latest, destinationPath, overwrite: true);
    }
}
