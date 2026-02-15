//777592
using System;
using System.Collections.Generic;

namespace WorldServer.networking
{
    public class VoicePrioritySettings
    {
        public bool EnablePriority { get; set; } = false;
        public int MaxPriorityPlayers { get; set; } = 10;
        public float PriorityVolume { get; set; } = 1.0f;
        public float NonPriorityVolume { get; set; } = 0.2f;
        public bool GuildMembersGetPriority { get; set; } = true;
        public bool LockedPlayersGetPriority { get; set; } = true;
        public int ActivationThreshold { get; set; } = 8;
        public HashSet<int> ManualPriorityList { get; set; } = new HashSet<int>();
        private readonly object _priorityLock = new object();

        public VoicePrioritySettings()
        {
        }

        public bool HasManualPriority(int accountId)
        {
            lock (_priorityLock)
            {
                return ManualPriorityList.Contains(accountId);
            }
        }

        public bool AddManualPriority(int accountId)
        {
            lock (_priorityLock)
            {
                if (ManualPriorityList.Count >= MaxPriorityPlayers)
                    return false;
                ManualPriorityList.Add(accountId);
                return true;
            }
        }

        public bool RemoveManualPriority(int accountId)
        {
            lock (_priorityLock)
            {
                return ManualPriorityList.Remove(accountId);
            }
        }

        public int GetManualPriorityCount()
        {
            lock (_priorityLock)
            {
                return ManualPriorityList.Count;
            }
        }

        // Clone settings (useful for database operations)
        // Clone settings (useful for database operations)
        public VoicePrioritySettings Clone()
        {
            return new VoicePrioritySettings
            {
                EnablePriority = this.EnablePriority,
                MaxPriorityPlayers = this.MaxPriorityPlayers,
                PriorityVolume = this.PriorityVolume,
                NonPriorityVolume = this.NonPriorityVolume,
                GuildMembersGetPriority = this.GuildMembersGetPriority,
                LockedPlayersGetPriority = this.LockedPlayersGetPriority,
                ManualPriorityList = new HashSet<int>(this.ManualPriorityList),
                ActivationThreshold = this.ActivationThreshold
            };
        }
        public bool ShouldFilterVoice(bool hasPriority)
        {
            if (!EnablePriority) 
                return false; // Priority system disabled, don't filter
    
            float volumeMultiplier = this.GetVolumeMultiplier(hasPriority);
            return volumeMultiplier <= 0.001f; // Volume effectively zero, filter completely
        }

// OPTIONAL: Add this method for debugging/logging
        public string GetFilterReason(bool hasPriority)
        {
            if (!EnablePriority) 
                return "Priority system disabled";
    
            if (hasPriority)
                return $"Priority player (volume: {PriorityVolume:F2})";
            else if (NonPriorityVolume <= 0.001f)
                return "Non-priority player filtered (volume: 0)";
            else
                return $"Non-priority player (volume: {NonPriorityVolume:F2})";
        }
        // Validate settings (ensure they're within reasonable bounds)
        // Validate settings (ensure they're within reasonable bounds)
        public void ValidateSettings()
        {
            // Ensure max players is within reasonable range
            if (MaxPriorityPlayers < 5) MaxPriorityPlayers = 5;
            if (MaxPriorityPlayers > 50) MaxPriorityPlayers = 50;

            // Ensure priority volume is between 0% and 200% (allow boosting)
            if (PriorityVolume < 0.0f) PriorityVolume = 0.0f;
            if (PriorityVolume > 2.0f) PriorityVolume = 2.0f;

            // Ensure non-priority volume is between 0% and 100%
            if (NonPriorityVolume < 0.0f) NonPriorityVolume = 0.0f;
            if (NonPriorityVolume > 1.0f) NonPriorityVolume = 1.0f;

            // ADD THESE LINES:
            // Ensure activation threshold is reasonable
            if (ActivationThreshold < 3) ActivationThreshold = 3;
            if (ActivationThreshold > 30) ActivationThreshold = 30;

            // Trim manual priority list if it exceeds max
            lock (_priorityLock)
            {
                while (ManualPriorityList.Count > MaxPriorityPlayers)
                {
                    var enumerator = ManualPriorityList.GetEnumerator();
                    enumerator.MoveNext();
                    ManualPriorityList.Remove(enumerator.Current);
                }
            }
        }
    }

    // Extension methods to make priority operations cleaner
    public static class VoicePriorityExtensions
    {
        public static bool IsAtMaxCapacity(this VoicePrioritySettings settings)
        {
            return settings.ManualPriorityList.Count >= settings.MaxPriorityPlayers;
        }

        public static int GetAvailableSlots(this VoicePrioritySettings settings)
        {
            return Math.Max(0, settings.MaxPriorityPlayers - settings.ManualPriorityList.Count);
        }

        public static float GetVolumeMultiplier(this VoicePrioritySettings settings, bool hasPriority)
        {
            return hasPriority ? settings.PriorityVolume : settings.NonPriorityVolume;
        }
    }
}