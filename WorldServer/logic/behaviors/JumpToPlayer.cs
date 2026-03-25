using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Jumps to the nearest player, cycling through animation frames (altTexture min→max)
    /// proportional to travel time. Uses MoveUnchecked so it passes through no-walk tiles.
    /// On completion, snaps to the target position and sets CycleStatus.Completed.
    /// </summary>
    internal class JumpToPlayer : CycleBehavior
    {
        private readonly float _speed;
        private readonly float _range;
        private readonly int _texMin;
        private readonly int _texMax;

        /// <param name="speed">Movement speed (same units as Charge)</param>
        /// <param name="range">Max distance to acquire a player target</param>
        /// <param name="texMin">First animation frame index (jump start)</param>
        /// <param name="texMax">Last animation frame index (before landing)</param>
        public JumpToPlayer(double speed = 6, float range = 100, int texMin = 0, int texMax = 1)
        {
            _speed = (float)speed;
            _range = range;
            _texMin = texMin;
            _texMax = texMax;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            // Acquire target on state entry
            var player = host.GetNearestEntity(_range, null);
            if (player == null)
            {
                // No player — mark completed immediately so transition can fire
                state = new JumpState { Phase = JumpPhase.Done };
                Status = CycleStatus.Completed;
                return;
            }

            var dx = player.X - host.X;
            var dy = player.Y - host.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist < 0.5f)
            {
                state = new JumpState { Phase = JumpPhase.Done };
                Status = CycleStatus.Completed;
                return;
            }

            var travelMs = (int)(dist / host.GetSpeed(_speed) * 1000);
            if (travelMs < 100) travelMs = 100; // minimum jump time

            var totalFrames = _texMax - _texMin + 1;
            var msPerFrame = travelMs / totalFrames;
            if (msPerFrame < 50) msPerFrame = 50; // minimum frame time

            var s = new JumpState
            {
                Phase = JumpPhase.Jumping,
                TargetX = player.X,
                TargetY = player.Y,
                StartX = host.X,
                StartY = host.Y,
                TotalMs = travelMs,
                ElapsedMs = 0,
                CurrentFrame = _texMin,
                MsPerFrame = msPerFrame,
                FrameElapsed = 0
            };

            host.AltTextureIndex = _texMin;
            state = s;
            Status = CycleStatus.InProgress;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as JumpState;
            if (s == null || s.Phase == JumpPhase.Done)
            {
                Status = CycleStatus.Completed;
                return;
            }

            if (host.HasConditionEffect(ConditionEffectIndex.Paralyzed))
            {
                // Still tick elapsed so we don't get stuck
                s.ElapsedMs += time.ElapsedMsDelta;
            }
            else
            {
                s.ElapsedMs += time.ElapsedMsDelta;
            }

            // Animation frame cycling based on elapsed time
            s.FrameElapsed += time.ElapsedMsDelta;
            if (s.FrameElapsed >= s.MsPerFrame && s.CurrentFrame < _texMax)
            {
                s.CurrentFrame++;
                host.AltTextureIndex = s.CurrentFrame;
                s.FrameElapsed -= s.MsPerFrame;
            }

            // Interpolate position (arc movement — lerp through no-walk tiles)
            var t = Math.Clamp((float)s.ElapsedMs / s.TotalMs, 0f, 1f);
            var newX = s.StartX + (s.TargetX - s.StartX) * t;
            var newY = s.StartY + (s.TargetY - s.StartY) * t;

            // MoveUnchecked bypasses no-walk tile validation
            host.MoveUnchecked(newX, newY);

            Status = CycleStatus.InProgress;

            // Jump complete — snap to target and reset to normal texture
            if (s.ElapsedMs >= s.TotalMs)
            {
                host.MoveUnchecked(s.TargetX, s.TargetY);
                host.AltTextureIndex = 0;
                s.Phase = JumpPhase.Done;
                Status = CycleStatus.Completed;
            }

            state = s;
        }

        private enum JumpPhase
        {
            Jumping,
            Done
        }

        private class JumpState
        {
            public JumpPhase Phase;
            public float TargetX, TargetY;
            public float StartX, StartY;
            public int TotalMs;
            public int ElapsedMs;
            public int CurrentFrame;
            public int MsPerFrame;
            public int FrameElapsed;
        }
    }
}
