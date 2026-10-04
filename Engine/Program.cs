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
        static void Main()
        {
            try
            {
                using (var game = new Engine())
                    game.Run();
            }
            catch (Exception ex)
            {
                // WinExe has no console, so an unhandled exception would otherwise vanish.
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"), ex.ToString()); }
                catch { }
                throw;
            }
        }
    }
#endif
}
