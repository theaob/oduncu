using System;
using System.Collections.Generic;
using System.Diagnostics;
using Oduncu.Sim;
using Oduncu.Sim.AI;
using UnityEngine;

namespace Oduncu.Game
{
    /// <summary>
    /// Owns the Simulation and advances it at a fixed tick rate from Update. Presentation
    /// reads state after each tick; input enqueues Commands that are applied on the next tick.
    /// Every player other than the local one is driven by a Standard AI, which emits commands
    /// through the same queue (plan section 3.6).
    /// </summary>
    public sealed class SimRunner : MonoBehaviour
    {
        [Tooltip("Player index controlled by touch input on this device.")]
        public int LocalPlayer = 0;

        [Tooltip("Log the state hash every 100 ticks (development builds only).")]
        public bool LogHashes = true;

        public Simulation Sim { get; private set; }

        public int Seed { get; private set; }

        /// <summary>Single player pause: the runner stops stepping (plan section 4).</summary>
        public bool Paused { get; set; }

        /// <summary>Fraction of the way from the previous tick to the next, for interpolation.</summary>
        public float InterpolationAlpha { get; private set; }

        /// <summary>Milliseconds the last tick took, AI included, for the debug overlay.</summary>
        public float LastTickMs { get; private set; }

        /// <summary>Slowest tick in the last 100, for the debug overlay.</summary>
        public float PeakTickMs { get; private set; }

        /// <summary>State hash at the last multiple of 100 ticks.</summary>
        public ulong LastHash { get; private set; }

        /// <summary>Raised after every simulation tick.</summary>
        public event Action<Simulation> Ticked;

        /// <summary>Raised when a new match replaces the previous one.</summary>
        public event Action<Simulation> MatchStarted;

        private readonly List<Command> _pending = new List<Command>();
        private readonly List<Command> _stepCommands = new List<Command>();
        private readonly Stopwatch _watch = new Stopwatch();
        private StandardAI[] _ais = Array.Empty<StandardAI>();
        private float _accumulator;
        private float _peakWindow;

        public bool HasMatch => Sim != null;

        /// <summary>Start a skirmish on the Open map against Standard AIs.</summary>
        public void StartMatch(int seed)
        {
            Seed = seed;
            Sim = OpenMap.Create(seed);
            _ais = new StandardAI[Sim.Players.Length];
            for (int p = 0; p < _ais.Length; p++)
            {
                if (p != LocalPlayer) _ais[p] = new StandardAI(p, Sim.Rng.State);
            }
            _pending.Clear();
            _accumulator = 0f;
            Paused = false;
            LastHash = Sim.ComputeHash();
            MatchStarted?.Invoke(Sim);
        }

        public void EndMatch()
        {
            Sim = null;
            _ais = Array.Empty<StandardAI>();
            _pending.Clear();
        }

        public void Enqueue(Command command)
        {
            if (command != null) _pending.Add(command);
        }

        private void Update()
        {
            if (Sim == null || Paused || Sim.MatchOver)
            {
                InterpolationAlpha = 1f;
                return;
            }

            const float tickSeconds = 1f / SimConstants.TicksPerSecond;
            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= tickSeconds && steps < SimConstants.MaxCatchUpTicks && !Sim.MatchOver)
            {
                _accumulator -= tickSeconds;
                StepOnce();
                steps++;
            }
            if (steps == SimConstants.MaxCatchUpTicks) _accumulator = 0f;
            InterpolationAlpha = Mathf.Clamp01(_accumulator / tickSeconds);
        }

        private void StepOnce()
        {
            _watch.Restart();
            _stepCommands.Clear();
            _stepCommands.AddRange(_pending);
            _pending.Clear();
            for (int p = 0; p < _ais.Length; p++)
            {
                if (_ais[p] != null) _ais[p].Think(Sim, _stepCommands);
            }
            Sim.Step(_stepCommands);
            _watch.Stop();

            LastTickMs = (float)_watch.Elapsed.TotalMilliseconds;
            _peakWindow = Mathf.Max(_peakWindow, LastTickMs);
            if (Sim.CurrentTick % 100 == 0)
            {
                PeakTickMs = _peakWindow;
                _peakWindow = 0f;
                LastHash = Sim.ComputeHash();
                if (LogHashes && UnityEngine.Debug.isDebugBuild)
                {
                    UnityEngine.Debug.Log("tick " + Sim.CurrentTick + " hash " + LastHash.ToString("X16"));
                }
            }
            Ticked?.Invoke(Sim);
        }
    }
}
