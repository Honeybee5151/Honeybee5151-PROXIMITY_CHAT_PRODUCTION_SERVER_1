using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Moves an entity along the perimeter of a square centered on its parent entity (or nearest target).
    /// The square follows the parent dynamically. Each ball starts at a different corner based on startCorner.
    /// </summary>
    internal class SquareOrbit : CycleBehavior
    {
        private readonly float _speed;
        private readonly float _halfSize; // half the side length of the square
        private readonly float _acquireRange;
        private readonly int _startCorner; // 0-3, which corner to start at
        private readonly bool _clockwise;
        private ushort? _target;

        /// <param name="speed">Movement speed along the square edge</param>
        /// <param name="sideLength">Full side length of the square</param>
        /// <param name="startCorner">Which corner to start at (0=top-left, 1=top-right, 2=bottom-right, 3=bottom-left)</param>
        /// <param name="acquireRange">Range to find parent/target entity</param>
        /// <param name="target">Target entity name to orbit around (null = use ParentEntity)</param>
        /// <param name="clockwise">Direction of orbit</param>
        public SquareOrbit(double speed = 3, double sideLength = 6, int startCorner = 0,
            double acquireRange = 100, string target = null, bool clockwise = true)
        {
            _speed = (float)speed;
            _halfSize = (float)(sideLength / 2.0);
            _startCorner = startCorner;
            _acquireRange = (float)acquireRange;
            _clockwise = clockwise;
            _target = target == null ? null : GetObjType(target);
        }

        // Square corners relative to center (CW order): TL, TR, BR, BL
        // Corner 0 = (-half, -half) = top-left
        // Corner 1 = (+half, -half) = top-right
        // Corner 2 = (+half, +half) = bottom-right
        // Corner 3 = (-half, +half) = bottom-left
        private void GetCorner(int index, out float rx, out float ry)
        {
            switch (index % 4)
            {
                case 0: rx = -_halfSize; ry = -_halfSize; break;
                case 1: rx = _halfSize;  ry = -_halfSize; break;
                case 2: rx = _halfSize;  ry = _halfSize;  break;
                case 3: rx = -_halfSize; ry = _halfSize;  break;
                default: rx = 0; ry = 0; break;
            }
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            // Distribute balls evenly along the entire square perimeter using entity ID
            var sideLength = _halfSize * 2f;
            var perimeter = sideLength * 4f;
            // Each ball gets an evenly-spaced starting position along the perimeter
            var slot = _startCorner >= 0 ? _startCorner : (int)(host.Id % 8);
            var startDist = (perimeter / 8f) * slot;
            // Convert perimeter distance to corner + edge progress
            var corner = (int)(startDist / sideLength);
            var edgeProgress = startDist - corner * sideLength;
            state = new SquareOrbitState
            {
                CurrentCorner = corner % 4,
                EdgeProgress = edgeProgress
            };
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = state as SquareOrbitState ?? new SquareOrbitState { CurrentCorner = _startCorner };

            Status = CycleStatus.NotStarted;

            if (host.HasConditionEffect(ConditionEffectIndex.Paralyzed))
            {
                state = s;
                return;
            }

            // Find center entity (parent or target)
            Entity center = null;
            if (host is Enemy enemy && enemy.ParentEntity != null && !enemy.ParentEntity.Dead)
            {
                center = enemy.ParentEntity;
            }
            else
            {
                center = host.GetNearestEntity(_acquireRange, _target);
            }

            if (center == null)
            {
                state = s;
                return;
            }

            // Calculate movement along the square edge
            var dist = host.GetSpeed(_speed) * time.DeltaTime;
            s.EdgeProgress += dist;

            var sideLength = _halfSize * 2f;

            // Advance to next edge if we've completed this one
            while (s.EdgeProgress >= sideLength)
            {
                s.EdgeProgress -= sideLength;
                s.CurrentCorner = _clockwise
                    ? (s.CurrentCorner + 1) % 4
                    : (s.CurrentCorner + 3) % 4; // +3 mod 4 = -1 mod 4
            }

            // Interpolate position between current corner and next corner
            var nextCorner = _clockwise
                ? (s.CurrentCorner + 1) % 4
                : (s.CurrentCorner + 3) % 4;

            GetCorner(s.CurrentCorner, out var cx, out var cy);
            GetCorner(nextCorner, out var nx, out var ny);

            var t = s.EdgeProgress / sideLength;
            var targetX = center.X + cx + (nx - cx) * t;
            var targetY = center.Y + cy + (ny - cy) * t;

            // Move directly to calculated position (unchecked so balls pass through walls)
            host.MoveUnchecked(targetX, targetY);

            Status = CycleStatus.InProgress;
            state = s;
        }

        private class SquareOrbitState
        {
            public int CurrentCorner;
            public float EdgeProgress; // how far along the current edge (0 to sideLength)
        }
    }
}
