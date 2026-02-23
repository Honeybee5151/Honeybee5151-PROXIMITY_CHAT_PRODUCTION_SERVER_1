using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Shared.resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    internal static class JsonBehaviorLoader
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void LoadAll(BehaviorDb db, string resourcePath)
        {
            var dir = Path.Combine(resourcePath, "behaviors", "community");
            if (!Directory.Exists(dir))
                return;

            var files = Directory.GetFiles(dir, "*.json");
            var total = 0;

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var mobBehaviors = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(json);
                    if (mobBehaviors == null) continue;

                    foreach (var kvp in mobBehaviors)
                    {
                        if (RegisterBehavior(db, kvp.Key, kvp.Value))
                            total++;
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[JsonBehavior] Failed to load {Path.GetFileName(file)}: {e.Message}");
                }
            }

            if (total > 0)
                Log.Info($"[JsonBehavior] Loaded {total} community mob behaviors.");
        }

        private static bool RegisterBehavior(BehaviorDb db, string mobName, JObject def)
        {
            var data = db.GameServer.Resources.GameData;

            if (!data.IdToObjectType.TryGetValue(mobName, out var type))
            {
                Log.Warn($"[JsonBehavior] Mob '{mobName}' not found in XML data, skipping.");
                return false;
            }

            if (db.Definitions.ContainsKey(type))
                return false; // don't override hardcoded behaviors

            try
            {
                var rootState = BuildStateTree(def);
                var d = new Dictionary<string, State>();
                rootState.Resolve(d);
                rootState.ResolveChildren(d);
                db.Definitions[type] = new Tuple<State, Loot>(rootState, null);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[JsonBehavior] Failed to build behavior for '{mobName}': {e.Message}");
                return false;
            }
        }

        private static State BuildStateTree(JObject def)
        {
            var statesObj = def["states"] as JObject;
            var initialState = def["initialState"]?.ToString() ?? "idle";

            if (statesObj == null || statesObj.Count == 0)
                return new State("root", new Wander(0.4));

            var childStates = new List<IStateChildren>();

            foreach (var prop in statesObj.Properties())
            {
                var stateName = prop.Name;
                var stateData = prop.Value as JObject;
                if (stateData == null) continue;

                var children = new List<IStateChildren>();

                // Parse behaviors
                var behaviorsArr = stateData["behaviors"] as JArray;
                if (behaviorsArr != null)
                {
                    foreach (var b in behaviorsArr)
                    {
                        var behavior = ParseBehavior(b as JObject);
                        if (behavior != null)
                            children.Add(behavior);
                    }
                }

                // Parse transitions
                var transitionsArr = stateData["transitions"] as JArray;
                if (transitionsArr != null)
                {
                    foreach (var t in transitionsArr)
                    {
                        var transition = ParseTransition(t as JObject);
                        if (transition != null)
                            children.Add(transition);
                    }
                }

                childStates.Add(new State(stateName, children.ToArray()));
            }

            return new State("root", childStates.ToArray());
        }

        private static Behavior ParseBehavior(JObject b)
        {
            if (b == null) return null;

            var type = b["type"]?.ToString();
            if (type == null) return null;

            try
            {
                switch (type)
                {
                    case "Wander":
                        return new Wander(
                            b["speed"]?.Value<double>() ?? 0.4
                        );

                    case "Chase":
                        return new Chase(
                            speed: b["speed"]?.Value<double>() ?? 10,
                            sightRange: b["sightRange"]?.Value<double>() ?? 10.5,
                            range: b["range"]?.Value<double>() ?? 1
                        );

                    case "Follow":
                        return new Follow(
                            speed: b["speed"]?.Value<double>() ?? 0.6,
                            acquireRange: b["acquireRange"]?.Value<double>() ?? 10,
                            range: b["range"]?.Value<double>() ?? 6
                        );

                    case "Shoot":
                        return new Shoot(
                            radius: b["radius"]?.Value<double>() ?? 8,
                            count: b["count"]?.Value<int>() ?? 1,
                            shootAngle: b["shootAngle"]?.Value<double>(),
                            projectileIndex: b["projectileIndex"]?.Value<int>() ?? 0,
                            fixedAngle: b["fixedAngle"]?.Value<double>(),
                            rotateAngle: b["rotateAngle"]?.Value<double>(),
                            predictive: b["predictive"]?.Value<double>() ?? 0,
                            coolDown: new Cooldown(b["coolDown"]?.Value<int>() ?? 1000, 0)
                        );

                    case "Orbit":
                        return new Orbit(
                            speed: b["speed"]?.Value<double>() ?? 1,
                            radius: b["radius"]?.Value<double>() ?? 3
                        );

                    case "Charge":
                        return new Charge(
                            speed: b["speed"]?.Value<double>() ?? 4,
                            range: (float)(b["range"]?.Value<double>() ?? 10),
                            coolDown: new Cooldown(b["coolDown"]?.Value<int>() ?? 2000, 0)
                        );

                    case "Spawn":
                        return new Spawn(
                            children: b["child"]?.ToString() ?? "",
                            maxChildren: b["maxChildren"]?.Value<int>() ?? 5,
                            coolDown: new Cooldown(b["coolDown"]?.Value<int>() ?? 3000, 0)
                        );

                    case "HealSelf":
                        return new HealSelf(
                            coolDown: new Cooldown(b["coolDown"]?.Value<int>() ?? 5000, 0),
                            amount: b["amount"]?.Value<int>()
                        );

                    case "Grenade":
                        return new Grenade(
                            radius: b["radius"]?.Value<double>() ?? 3,
                            damage: b["damage"]?.Value<int>() ?? 100,
                            range: b["range"]?.Value<double>() ?? 5,
                            coolDown: new Cooldown(b["coolDown"]?.Value<int>() ?? 2000, 0)
                        );

                    case "Flash":
                        return new Flash(
                            color: b["color"]?.Value<uint>() ?? 0xff0000,
                            flashPeriod: b["period"]?.Value<double>() ?? 0.2,
                            flashRepeats: b["repeats"]?.Value<int>() ?? 5
                        );

                    case "Taunt":
                        var texts = b["text"]?.ToObject<string[]>() ?? new[] { "..." };
                        return new Taunt(texts);

                    case "ChangeSize":
                        return new ChangeSize(
                            rate: b["rate"]?.Value<int>() ?? 20,
                            target: b["max"]?.Value<int>() ?? 120
                        );

                    case "Invulnerable":
                    {
                        var duration = b["duration"]?.Value<int>() ?? -1;
                        return new ConditionEffectBehavior(
                            ConditionEffectIndex.Invulnerable,
                            perm: duration <= 0,
                            duration: duration
                        );
                    }

                    default:
                        Log.Warn($"[JsonBehavior] Unknown behavior type: {type}");
                        return null;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[JsonBehavior] Error parsing behavior '{type}': {e.Message}");
                return null;
            }
        }

        private static Transition ParseTransition(JObject t)
        {
            if (t == null) return null;

            var type = t["type"]?.ToString();
            var target = t["target"]?.ToString() ?? "";
            if (type == null) return null;

            try
            {
                switch (type)
                {
                    case "TimedTransition":
                        return new TimedTransition(
                            time: t["time"]?.Value<int>() ?? 5000,
                            targetState: target
                        );

                    case "HpLessTransition":
                        return new HpLessTransition(
                            threshold: t["threshold"]?.Value<double>() ?? 0.5,
                            targetState: target
                        );

                    case "PlayerWithinTransition":
                        return new PlayerWithinTransition(
                            dist: t["dist"]?.Value<double>() ?? 5,
                            targetState: target
                        );

                    case "NoPlayerWithinTransition":
                        return new NoPlayerWithinTransition(
                            dist: t["dist"]?.Value<double>() ?? 10,
                            targetState: target
                        );

                    default:
                        Log.Warn($"[JsonBehavior] Unknown transition type: {type}");
                        return null;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[JsonBehavior] Error parsing transition '{type}': {e.Message}");
                return null;
            }
        }
    }
}
