using System;
using System.Collections.Generic;
using System.IO;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Runs the scripted determinism scenario on the device and logs the resulting hash.
    /// Milestone 0 exit criterion: two different phones print the same DETERMINISM_HASH
    /// line, and it matches the value the headless test suite produces on a PC.
    /// Enable with the inspector toggle or the -determinism command line argument.
    /// </summary>
    public sealed class DeterminismProbe : MonoBehaviour
    {
        public bool RunOnStart;
        public int Ticks = Scenarios.DeterminismTicks;

        private void Start()
        {
            bool fromArgs = Array.IndexOf(Environment.GetCommandLineArgs(), "-determinism") >= 0;
            if (!RunOnStart && !fromArgs) return;
            Run();
        }

        [ContextMenu("Run determinism probe")]
        public void Run()
        {
            var checkpoints = new List<ulong>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ulong hash = Scenarios.RunScripted(Scenarios.CreateDeterminismScenario(), Ticks, null, checkpoints);
            watch.Stop();

            string line = "DETERMINISM_HASH " + hash.ToString("X16") + " ticks=" + Ticks + " ms=" + watch.ElapsedMilliseconds
                          + " device=" + SystemInfo.deviceModel + " os=" + SystemInfo.operatingSystem;
            Debug.Log(line);
            for (int i = 0; i < checkpoints.Count; i++) Debug.Log("DETERMINISM_CHECKPOINT " + ((i + 1) * 1000) + " " + checkpoints[i].ToString("X16"));

            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "determinism.txt"), line + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not write determinism.txt: " + e.Message);
            }
        }
    }
}
