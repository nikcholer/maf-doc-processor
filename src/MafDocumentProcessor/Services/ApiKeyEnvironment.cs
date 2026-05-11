namespace MafDocumentProcessor.Services;

public static class ApiKeyEnvironment
{
    public static string? GetApiKey(string environmentVariableName)
    {
        var processValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(processValue))
        {
            return processValue;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var userValue = Environment.GetEnvironmentVariable(
            environmentVariableName,
            EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(userValue))
        {
            return userValue;
        }

        return Environment.GetEnvironmentVariable(
            environmentVariableName,
            EnvironmentVariableTarget.Machine);
    }

    public static bool HasApiKey(string environmentVariableName)
    {
        return !string.IsNullOrWhiteSpace(GetApiKey(environmentVariableName));
    }
}
