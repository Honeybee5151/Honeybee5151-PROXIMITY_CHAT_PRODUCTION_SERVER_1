using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace WorldServer.networking
{
    /// <summary>
    /// Spatial hash grid for O(1) proximity lookups instead of iterating all players.
    /// Cell size = voice range, so checking 9 cells (self + 8 neighbors) covers all possible listeners.
    /// </summary>
    public class SpatialGrid
    {
        private readonly float cellSize;
        private readonly ConcurrentDictionary<long, ConcurrentDictionary<string, PlayerPosition>> cells = new();

        public SpatialGrid(float cellSize)
        {
            this.cellSize = cellSize;
        }

        /// <summary>
        /// Converts world coordinates to a single 64-bit cell key.
        /// High 32 bits = cellX, low 32 bits = cellY.
        /// </summary>
        private long GetCellKey(float x, float y)
        {
            int cellX = (int)Math.Floor(x / cellSize);
            int cellY = (int)Math.Floor(y / cellSize);
            return ((long)cellX << 32) | (uint)cellY;
        }

        /// <summary>
        /// Update a player's position in the grid. Removes from old cell if moved.
        /// </summary>
        public void UpdatePlayer(string playerId, PlayerPosition position)
        {
            // Remove from any existing cell first
            RemovePlayer(playerId);

            var key = GetCellKey(position.X, position.Y);
            var cell = cells.GetOrAdd(key, _ => new ConcurrentDictionary<string, PlayerPosition>());
            cell[playerId] = position;
        }

        /// <summary>
        /// Remove a player from the grid entirely.
        /// </summary>
        public void RemovePlayer(string playerId)
        {
            foreach (var cell in cells.Values)
            {
                cell.TryRemove(playerId, out _);
            }
        }

        /// <summary>
        /// Get all players in the same cell and 8 neighboring cells, filtered by world ID.
        /// Returns candidates that are within maxRange — caller still does exact distance check.
        /// </summary>
        public List<(string PlayerId, PlayerPosition Position, float Distance)> GetNearbyPlayers(
            float x, float y, float maxRange, int worldId)
        {
            var results = new List<(string, PlayerPosition, float)>();
            int centerCellX = (int)Math.Floor(x / cellSize);
            int centerCellY = (int)Math.Floor(y / cellSize);

            // Check 3x3 grid of cells (self + 8 neighbors)
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    long key = ((long)(centerCellX + dx) << 32) | (uint)(centerCellY + dy);
                    if (!cells.TryGetValue(key, out var cell))
                        continue;

                    foreach (var kvp in cell)
                    {
                        var pos = kvp.Value;
                        if (pos.WorldId != worldId)
                            continue;

                        float distX = x - pos.X;
                        float distY = y - pos.Y;
                        float distance = (float)Math.Sqrt(distX * distX + distY * distY);

                        if (distance <= maxRange)
                        {
                            results.Add((kvp.Key, pos, distance));
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Clean up empty cells to prevent memory growth.
        /// </summary>
        public void CleanupEmptyCells()
        {
            foreach (var kvp in cells)
            {
                if (kvp.Value.IsEmpty)
                    cells.TryRemove(kvp.Key, out _);
            }
        }
    }
}
