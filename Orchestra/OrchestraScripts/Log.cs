using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// One prefix in one place, so the mod's lines can be found in `Player.log` and
    /// in the in-game console with `show_logs true`.
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[Orchestra] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }
    }
}
