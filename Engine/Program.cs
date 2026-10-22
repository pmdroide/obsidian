using System;

namespace Engine
{
#if WINDOWS || LINUX
    /// <summary>
    /// The main class.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Logic.CrashHandler.Install("Obsidian", args);
            try
            {
                using (var game = new Engine())
                    game.Run();
            }
            catch (Exception ex)
            {
                // WinExe has no console, so an unhandled exception would otherwise vanish.
                // Report here (game-thread state still intact), then rethrow so Windows Error Reporting sees it.
                Logic.CrashHandler.ReportFatal(ex, "Engine main loop");
                throw;
            }
        }
    }
#endif
}
