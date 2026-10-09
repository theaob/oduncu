using System;
using System.Collections.Generic;
using Oduncu.Sim;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Owns the Simulation and advances it at a fixed tick rate from Update. Presentation
    /// reads state after each tick; input enqueues Commands that are applied on the next tick.
    /// </summary>
    public sealed class SimRunner : MonoBehaviour
    {
        [Tooltip("Map seed. The same seed always produces the same map.")]
        public int Seed = 12345;

        [Tooltip("Player index controlled by touch input on this device.")]
        public int LocalPlayer = 0;

        [Tooltip("Log the state hash every 100 ticks (development builds only).")]
        public bool LogHashes = true;

        public Simulation Sim { get; private set; }

        /// <summary>Fraction of the way from the previous tick to the next, for interpolation.</summary>
        public float InterpolationAlpha { get; private set; }

        /// <summary>Raised after every simulation tick.</summary>
        public event Action<Simulation> Ticked;

        private readonly List<Command> _pending = new List<Command>();
        private float _accumulator;

        private void Awake()
        {
            Sim = OpenMap.Create(Seed);
        }

        public void Enqueue(Command command)
        {
            if (command != null) _pending.Add(command);
        }

        private void Update()
        {
            const float tickSeconds = 1f / SimConstants.TicksPerSecond;
            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= tickSeconds && steps < SimConstants.MaxCatchUpTicks)
            {
                _accumulator -= tickSeconds;
                Sim.Step(_pending);
                _pending.Clear();
                steps++;
                Ticked?.Invoke(Sim);
                if (LogHashes && Debug.isDebugBuild && Sim.CurrentTick % 100 == 0)
                {
                    Debug.Log("tick " + Sim.CurrentTick + " hash " + Sim.ComputeHash().ToString("X16"));
                }
            }
            if (steps == SimConstants.MaxCatchUpTicks) _accumulator = 0f;
            InterpolationAlpha = Mathf.Clamp01(_accumulator / tickSeconds);
        }
    }
}
