using CMS.EventLog;

namespace CastlefordImporterHelpers
{
    public static class KenticoLogger
    {
        public static void LogError(string description)
        {
            EventLogProvider.LogEvent(
                EventType.ERROR,
                "Castleford Article Importer",
                "EXCEPTION",
                description
            );
        }

        public static void LogInfo(string description)
        {
            EventLogProvider.LogEvent(
                EventType.INFORMATION,
                "Castleford Article Importer",
                "INFO",
                description
            );
        }

        public static string ExitTask(string errorDescription)
        {
            LogError(errorDescription);
            return errorDescription;
        }
    }
}
