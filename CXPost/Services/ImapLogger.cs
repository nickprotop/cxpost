using SharpConsoleUI.Logging;

namespace CXPost.Services;

/// <summary>
/// Centralized IMAP debug logger. Writes to ConsoleEx LogService.
/// Enable file logging with: SHARPCONSOLEUI_DEBUG_LOG=/tmp/cxpost.log SHARPCONSOLEUI_DEBUG_LEVEL=Debug
/// </summary>
public static class ImapLogger
{
    private static ILogService? _logService;
    private const string Category = "IMAP";

    public static void Init(ILogService logService) => _logService = logService;

    public static void Debug(string message) => _logService?.LogDebug(message, Category);
    public static void Info(string message) => _logService?.LogInfo(message, Category);
    public static void Warn(string message) => _logService?.LogWarning(message, Category);
    public static void Error(string message, Exception? ex = null) => _logService?.LogError(message, ex, Category);
}
