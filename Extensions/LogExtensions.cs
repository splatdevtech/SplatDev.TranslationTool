using Microsoft.Extensions.Logging;

namespace SplatDev.TranslationTool.Extensions;

public static class LogExtensions
{
    public static void LogInformationLazy(this ILogger logger, string message, Func<object[]>? resolveParameters = null)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            if (resolveParameters == null)
            {
                logger.LogInformation(message);
            }
            else
            {
                logger.LogInformation(message, resolveParameters());
            }
        }
    }
}
