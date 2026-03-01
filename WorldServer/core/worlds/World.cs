using Pipelines.Sockets.Unofficial.Arenas;
using Shared.database;
using Shared.resources;
using System.Xml.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.objects.containers;
using WorldServer.core.objects.vendors;
using WorldServer.core.structures;
using WorldServer.core.terrain;
using WorldServer.core.worlds.census;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.worlds
{
    public class World
    {
        public const int CULL_RANGE = 30;
        
        public const int NEXUS_ID = -2;
        public const int ARENA_ID = -3;
        public const int TEST_ID = -4;

        private int NextEntityId;

        public int Id { get; }
        public string IdName { get; set; }
        public string DisplayName { get; set; }
        public WorldResourceInstanceType InstanceType { get; private set; }
        public bool Persist { get; private set; }
        public int MaxPlayers { get; protected set; }
        public bool CreateInstance { get; private set; }
        public bool IsRealm { get; set; }
        public bool AllowTeleport { get; protected set; }
        public byte Background { get; protected set; }
        public byte Blocking { get; protected set; }
        public string Music { get; set; }
        public byte Difficulty { get; protected set; }
        public bool Deleted { get; protected set; }
        public bool DisableShooting { get; set; }
        public bool DisableAbilities { get; set; }
        private long Lifetime { get; set; }

        public bool isWeekend { get; set; } = false;
        public List<Shared.resources.CustomGroundEntry> CustomGroundEntries { get; set; }
        public List<Shared.resources.CustomObjectEntry> CustomObjectEntries { get; set; }
        /// <summary>Pre-compressed message blobs for custom grounds (one per chunk). Built once at load.</summary>
        public List<byte[]> PreCompressedGroundChunks { get; set; }
        /// <summary>Pre-compressed message blobs for custom objects (one per chunk). Built once at load.</summary>
        public List<byte[]> PreCompressedObjectChunks { get; set; }
        public string CustomDungeonAssetsXml { get; set; }
        /// <summary>Pre-encoded UTF-8 bytes of CustomDungeonAssetsXml (built once, reused per client)</summary>
        public byte[] PreEncodedDungeonAssetsBytes { get; set; }
        public bool IsCommunityDungeon { get; set; } = false;
        public string[] StartingEquipment { get; set; }
        public string[] InventoryItems { get; set; }
        public int PresetLevel { get; set; } = 1;
        public int[] PresetStats { get; set; }
        public int PresetHealthPotions { get; set; }
        public int PresetManaPotions { get; set; }
        public bool PresetHasBackpack { get; set; }

        public readonly Wmap Map;
        public readonly GameServer GameServer;
        public bool ShowDisplays { get; protected set; }

        public CollisionMap<Entity> EnemiesCollision { get; private set; }
        public CollisionMap<Entity> PlayersCollision { get; private set; }
        public Dictionary<int, Player> Players { get; private set; } = new Dictionary<int, Player>();
        public Dictionary<int, Enemy> Enemies { get; private set; } = new Dictionary<int, Enemy>();
        public Dictionary<int, Enemy> Quests { get; private set; } = new Dictionary<int, Enemy>();
        public Dictionary<int, StaticObject> StaticObjects { get; private set; } = new Dictionary<int, StaticObject>();
        public Dictionary<int, Container> Containers { get; private set; } = new Dictionary<int, Container>();
        public Dictionary<int, Portal> Portals { get; private set; } = new Dictionary<int, Portal>();
        public Dictionary<int, Pet> Pets { get; private set; } = new Dictionary<int, Pet>();
        public Dictionary<int, SellableObject> SellableObjects { get; private set; } = new Dictionary<int, SellableObject>();

        private readonly List<Entity> EntitiesToAdd = new List<Entity>();
        private readonly List<Entity> EntitiesToRemove = new List<Entity>();

        private readonly List<WorldTimer> Timers = new List<WorldTimer>();

        public WorldBranch WorldBranch { get; private set; }
        public World ParentWorld { get; set; }

        // used for behaviour system
        // Hashset to have non duplicates
        private readonly HashSet<string> Labels = new HashSet<string>();

        public World(GameServer gameServer, int id, WorldResource resource, World parent = null)
        {
            GameServer = gameServer;
            Map = new Wmap(this);

            Id = id;
            IdName = resource.DisplayName;
            DisplayName = resource.DisplayName;
            Difficulty = resource.Difficulty;
            Background = resource.Background;
            MaxPlayers = resource.Capacity;
            InstanceType = resource.Instance;
            Persist = resource.Persists;
            ShowDisplays = Id == -2 || resource.ShowDisplays;
            Blocking = resource.VisibilityType;
            AllowTeleport = resource.AllowTeleport;
            DisableShooting = resource.DisableShooting;
            DisableAbilities = resource.DisableAbilities;
            CreateInstance = resource.CreateInstance;

            IsRealm = false;

            if (resource.Music.Count > 0)
                Music = resource.Music[Random.Shared.Next(0, resource.Music.Count)];
            else
                Music = "sorc";

            WorldBranch = new WorldBranch(this);
            ParentWorld = parent;

            var day = DateTime.Now.DayOfWeek;
            if (day != DayOfWeek.Saturday && day != DayOfWeek.Sunday) { }
            else isWeekend = true;
        }

        public bool HasLabel(string labelName) => Labels.Contains(labelName.ToLower());
        public bool SetLabel(string labelName) => Labels.Equals(labelName.ToLower());
        public bool RemoveLabel(string labelName) => Labels.Remove(labelName.ToLower());

        public virtual bool AllowedAccess(Client client) => true;

        public void Broadcast(OutgoingMessage outgoingMessage)
        {
            foreach (var player in Players.Values)
                player.Client.SendPacket(outgoingMessage);
        }

        public void BroadcastIfVisible(List<OutgoingMessage> outgoingMessages, ref Position worldPosData)
        {
            foreach (var outgoingMessage in outgoingMessages)
                BroadcastIfVisible(outgoingMessage, ref worldPosData);
        }

        public void BroadcastIfVisible(OutgoingMessage outgoingMessage, ref Position worldPosData)
        {
            foreach (var player in Players.Values)
                if (player.SqDistTo(ref worldPosData) < CULL_RANGE * CULL_RANGE)
                {
                    if (outgoingMessage is ServerPlayerShoot)
                        player.ServerPlayerShoot(outgoingMessage as ServerPlayerShoot);
                    player.Client.SendPacket(outgoingMessage);
                }
        }

        public void BroadcastIfVisible(OutgoingMessage outgoingMessage, Entity host)
        {
            foreach (var player in Players.Values)
                if (player.SqDistTo(host) < CULL_RANGE * CULL_RANGE)
                {
                    if (outgoingMessage is EnemyShootMessage)
                        player.ProcessEnemyShoot(outgoingMessage as EnemyShootMessage);
                    player.Client.SendPacket(outgoingMessage);
                }
        }

        public void BroadcastEnemyShootIfVisible(EnemyShootMessage enemyShoot, Entity host)
        {
            foreach (var player in Players.Values)
                if (player.SqDistTo(host) < CULL_RANGE * CULL_RANGE)
                {
                    player.ProcessEnemyShoot(enemyShoot);
                    player.Client.SendPacket(enemyShoot);
                }
        }

        public void BroadcastServerPlayerShoot(ServerPlayerShoot serverPlayerShoot, Entity host)
        {
            foreach (var player in Players.Values)
                if (player.SqDistTo(host) < CULL_RANGE * CULL_RANGE)
                {
                    player.ServerPlayerShoot(serverPlayerShoot);
                    player.Client.SendPacket(serverPlayerShoot);
                }
        }

        public void BroadcastServerPlayerShoot(List<ServerPlayerShoot> serverPlayerShoots, Entity host)
        {
            foreach (var player in Players.Values)
                if (player.SqDistTo(host) < CULL_RANGE * CULL_RANGE)
                {
                    foreach (var serverPlayerShoot in serverPlayerShoots)
                        player.ServerPlayerShoot(serverPlayerShoot);
                    player.Client.SendPackets(serverPlayerShoots);
                }
        }

        public void BroadcastIfVisibleExclude(List<OutgoingMessage> outgoingMessage, Entity broadcaster, Entity exclude)
        {
            foreach (var player in Players.Values)
                if (player.Id != exclude.Id && player.SqDistTo(broadcaster) <= CULL_RANGE * CULL_RANGE)
                    player.Client.SendPackets(outgoingMessage);
        }

        public void BroadcastIfVisibleExclude(OutgoingMessage outgoingMessage, Entity broadcaster, Entity exclude)
        {
            foreach (var player in Players.Values)
                if (player.Id != exclude.Id && player.SqDistTo(broadcaster) <= CULL_RANGE * CULL_RANGE)
                    player.Client.SendPacket(outgoingMessage);
        }

        public void BroadcastToPlayer(OutgoingMessage outgoingMessage, int playerId)
        {
            foreach (var player in Players.Values)
                if (player.Id == playerId)
                {
                    player.Client.SendPacket(outgoingMessage);
                    break;
                }
        }

        public void BroadcastToPlayers(OutgoingMessage outgoingMessage, List<int> playerIds)
        {
            foreach (var player in Players.Values)
                if (playerIds.Contains(player.Id))
                    player.Client.SendPacket(outgoingMessage);
        }

        public void ChatReceived(Player player, string text)
        {
            foreach (var en in Enemies)
                en.Value.OnChatTextReceived(player, text);
            foreach (var en in StaticObjects)
                en.Value.OnChatTextReceived(player, text);
        }

        public Player CreateNewPlayer(Client client, ushort type, float x, float y)
        {
            var entity = new Player(GameServer, client, type);
            entity.Id = GetNextEntityId();
            entity.Init(this);
            entity.Move(x, y);
            EntitiesToAdd.Add(entity);
            return entity;
        }

        public Entity CreateNewEntity(string idName, float x, float y) => !GameServer.Resources.GameData.IdToObjectType.TryGetValue(idName, out var type) ? null : CreateNewEntity(type, x, y);
        public Entity CreateNewEntity(ushort objectType, float x, float y)
        {
            var entity = Entity.Resolve(GameServer, objectType);
            if (entity == null)
            {
                // unable to identify the entity return null;
                return null;
            }
            entity.Id = GetNextEntityId();
            entity.Init(this);
            entity.Move(x, y);
            EntitiesToAdd.Add(entity);
            return entity;
        }

        public string GetDisplayName() => DisplayName != null && DisplayName.Length > 0 ? DisplayName : IdName;

        public void GetPlayerCount(ref int count) => WorldBranch.GetPlayerCount(ref count);

        public Entity GetEntity(int id)
        {
            if (Players.TryGetValue(id, out var ret1))
                return ret1;

            if (Enemies.TryGetValue(id, out var ret2))
                return ret2;

            if (StaticObjects.TryGetValue(id, out var ret3))
                return ret3;

            if (Containers.TryGetValue(id, out var ret4))
                return ret4;

            if (Portals.TryGetValue(id, out var ret5))
                return ret5;

            if (SellableObjects.TryGetValue(id, out var ret6))
                return ret6;

            return null;
        }

        public int GetNextEntityId() => NextEntityId++;

        public IEnumerable<Player> GetPlayers() => Players.Values;

        public Position? GetRegionPosition(TileRegion region)
        {
            if (Map.Regions.All(t => t.Value != region))
                return null;

            var reg = Map.Regions.Single(t => t.Value == region);

            return new Position() { X = reg.Key.X, Y = reg.Key.Y };
        }

        public virtual KeyValuePair<IntPoint, TileRegion>[] GetSpawnPoints() => Map.Regions.Where(t => t.Value == TileRegion.Spawn).ToArray();
        public virtual KeyValuePair<IntPoint, TileRegion>[] GetRegionPoints(TileRegion region) => Map.Regions.Where(t => t.Value == region).ToArray();

        public Player GetUniqueNamedPlayer(string name)
        {
            if (Database.GuestNames.Contains(name))
                return null;

            foreach (var i in Players.Values)
                if (i.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (!i.NameChosen && !(this is TestWorld))
                        GameServer.Database.ReloadAccount(i.Client.Account);

                    if (i.Client.Account.NameChosen)
                        return i;
                    break;
                }

            return null;
        }

        public bool IsPassable(double x, double y, bool spawning = false)
        {
            var x_ = (int)x;
            var y_ = (int)y;

            if (!Map.Contains(x_, y_))
                return false;

            var tile = Map[x_, y_];

            var tileDesc = GameServer.Resources.GameData.Tiles[tile.TileId];
            if (tileDesc.NoWalk)
                return false;

            if (tile.ObjType != 0 && tile.ObjDesc != null)
                if (tile.ObjDesc.FullOccupy || tile.ObjDesc.EnemyOccupySquare || spawning && tile.ObjDesc.OccupySquare)
                    return false;

            return true;
        }

        public bool IsPlayersMax() => Players.Count >= MaxPlayers;

        public void EnterWorld(Entity entity)
        {
            if (entity.Id == -1)
                entity.Id = GetNextEntityId();
            if(entity.World == null)
                entity.Init(this);
            EntitiesToAdd.Add(entity);
        }

        public virtual void LeaveWorld(Entity entity) => entity.Expunge();

        public virtual void AddToWorld(Entity entity)
        {
            if (entity is Player)
            {
                Players.TryAdd(entity.Id, entity as Player);
                PlayersCollision.Insert(entity, entity.X, entity.Y);
            }
            else if (entity is SellableObject)
                SellableObjects.TryAdd(entity.Id, entity as SellableObject);
            else if (entity is Enemy)
            {
                Enemies.TryAdd(entity.Id, entity as Enemy);
                EnemiesCollision.Insert(entity, entity.X, entity.Y);
                if (entity.ObjectDesc.Quest)
                    Quests.TryAdd(entity.Id, entity as Enemy);
            }
            else if (entity is Container)
                Containers.TryAdd(entity.Id, entity as Container);
            else if (entity is Portal)
                Portals.TryAdd(entity.Id, entity as Portal);
            else if (entity is StaticObject)
            {
                StaticObjects.TryAdd(entity.Id, entity as StaticObject);
                if (entity is Decoy)
                    PlayersCollision.Insert(entity, entity.X, entity.Y);
                else
                    EnemiesCollision.Insert(entity, entity.X, entity.Y);
            }
            else if (entity is Pet)
            {
                Pets.TryAdd(entity.Id, entity as Pet);
                PlayersCollision.Insert(entity, entity.X, entity.Y);
            }
        }

        private void RemoveFromWorld(Entity entity)
        {
            if (entity is Player player)
            {
                Players.Remove(entity.Id);
                PlayersCollision.Remove(entity);

                // if in trade, cancel it...
                if (player != null && player.TradeTarget != null)
                    player.CancelTrade();

                if (player != null && player.Pet != null)
                    LeaveWorld(player.Pet);
            }
            else if (entity is SellableObject)
                SellableObjects.Remove(entity.Id);
            else if (entity.ObjectDesc.Enemy)
            {
                Enemies.Remove(entity.Id);
                EnemiesCollision.Remove(entity);
                if (entity.ObjectDesc.Quest)
                    Quests.Remove(entity.Id);
            }
            else if (entity is Container)
                Containers.Remove(entity.Id);
            else if (entity is Portal)
                Portals.Remove(entity.Id);
            else if (entity is StaticObject)
            {
                StaticObjects.Remove(entity.Id);
                if (entity is Decoy)
                    PlayersCollision.Remove(entity);
                else
                    EnemiesCollision.Remove(entity);
            }
            else if (entity is Pet)
            {
                Pets.Remove(entity.Id);
                PlayersCollision.Remove(entity);
            }
        }

        public void ForeachPlayer(Action<Player> action)
        {
            foreach (var player in Players.Values)
                action?.Invoke(player);
        }

        public void ObjsWithin(Entity host, double radius, List<Entity> enemies)
        {
            var radSqr = radius * radius;
            foreach (var enemy in EnemiesCollision.HitTest(host.X, host.Y, radius))
            {
                if (enemy.SqDistTo(host.X, host.Y) >= radSqr)
                    continue;
                enemies.Add(enemy);
            }
        }

        public void WorldAnnouncement(string message)
        {
            var announcement = string.Concat("<ANNOUNCMENT> ", message);
            foreach (var player in Players.Values)
                player.SendInfo(announcement);
        }

        protected void FromWorldMap(Stream dat)
        {
            NextEntityId += Map.Load(dat, NextEntityId) + 1;
            InitMap();
        }

        public bool LoadMapFromData(WorldResource worldResource)
        {
            var jmPath = worldResource.MapJM[Random.Shared.Next(0, worldResource.MapJM.Count)];
            var data = GameServer.Resources.GameData.GetWorldData(jmPath);
            if (data == null)
                return false;

            var gameData = GameServer.Resources.GameData;

            // Register custom object ObjectDescs BEFORE loading map, so Wmap.Load() can resolve names
            if (gameData.JmCustomObjects.TryGetValue(jmPath, out var customObjects))
            {
                CustomObjectEntries = customObjects;
                gameData.RegisterCustomObjects(customObjects);
            }

            FromWorldMap(new MemoryStream(data));

            // Store custom ground entries for this dungeon (sent as binary to client)
            if (gameData.JmCustomGrounds.TryGetValue(jmPath, out var customGrounds))
                CustomGroundEntries = customGrounds;

            StaticLogger.Instance.Info($"[CustomDebug] World {Id} loading '{jmPath}': " +
                $"grounds={CustomGroundEntries?.Count ?? 0}, objects={CustomObjectEntries?.Count ?? 0}");

            // Pre-compress custom message chunks once at load (reused per client connect)
            PreCompressCustomChunks();

            StaticLogger.Instance.Info($"[CustomDebug] World {Id} compressed: " +
                $"groundChunks={PreCompressedGroundChunks?.Count ?? 0} " +
                $"(sizes: {string.Join(",", PreCompressedGroundChunks?.Select(b => b.Length.ToString()) ?? Array.Empty<string>())}), " +
                $"objectChunks={PreCompressedObjectChunks?.Count ?? 0} " +
                $"(sizes: {string.Join(",", PreCompressedObjectChunks?.Select(b => b.Length.ToString()) ?? Array.Empty<string>())})");

            // Load pre-built dungeon assets (per-dungeon sprites + objects) if available
            if (gameData.DungeonAssetsXml.TryGetValue(jmPath, out var assetsXml))
            {
                // Auto-remap type codes that collide with globally loaded objects
                assetsXml = RemapCollidingTypeCodes(assetsXml, gameData);
                CustomDungeonAssetsXml = assetsXml;
                PreEncodedDungeonAssetsBytes = System.Text.Encoding.UTF8.GetBytes(assetsXml);
            }

            return true;
        }

        /// <summary>
        /// Auto-remap dungeon asset type codes that collide with globally loaded objects.
        /// Replaces colliding type="0xNNNN" values with unused codes, and updates any
        /// projectile references in behavior JSON that use the old type codes.
        /// </summary>
        private static string RemapCollidingTypeCodes(string assetsXml, XmlData gameData)
        {
            // Extract all type codes from the dungeon assets
            var typePattern = new System.Text.RegularExpressions.Regex(@"type=""0x([0-9a-fA-F]+)""");
            var matches = typePattern.Matches(assetsXml);
            var remaps = new Dictionary<ushort, ushort>(); // old → new

            // Collect all type codes used in this dungeon
            var dungeonTypes = new HashSet<ushort>();
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (ushort.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out ushort tc))
                    dungeonTypes.Add(tc);
            }

            // Find collisions with global objects
            foreach (var tc in dungeonTypes)
            {
                if (gameData.ObjectTypeToId.ContainsKey(tc))
                {
                    // This type code is already used globally — need to remap
                    remaps[tc] = 0; // placeholder, will assign below
                }
            }

            if (remaps.Count == 0)
                return assetsXml; // no collisions

            // Find unused type codes starting from 0x5000
            ushort nextFree = 0x5000;
            var allUsed = new HashSet<ushort>(gameData.ObjectTypeToId.Keys);
            foreach (var tc in dungeonTypes)
                allUsed.Add(tc);

            foreach (var oldCode in remaps.Keys.ToList())
            {
                while (allUsed.Contains(nextFree))
                    nextFree++;
                remaps[oldCode] = nextFree;
                allUsed.Add(nextFree);
                nextFree++;
            }

            // Apply remaps to the XML string
            foreach (var kvp in remaps)
            {
                var oldStr = $"type=\"0x{kvp.Key:x4}\"";
                var newStr = $"type=\"0x{kvp.Value:x4}\"";
                assetsXml = assetsXml.Replace(oldStr, newStr);
                Console.WriteLine($"[DungeonAssets] Remapped type 0x{kvp.Key:x4} -> 0x{kvp.Value:x4} (collision with global '{gameData.ObjectTypeToId[kvp.Key]}')");
            }

            return assetsXml;
        }

        protected void PreCompressCustomChunks()
        {
            const int chunkSize = 500;

            if (CustomGroundEntries != null && CustomGroundEntries.Count > 0)
            {
                PreCompressedGroundChunks = new List<byte[]>();
                for (int i = 0; i < CustomGroundEntries.Count; i += chunkSize)
                {
                    var chunk = CustomGroundEntries.GetRange(i, Math.Min(chunkSize, CustomGroundEntries.Count - i));
                    PreCompressedGroundChunks.Add(CompressGroundChunk(chunk));
                }
            }

            if (CustomObjectEntries != null && CustomObjectEntries.Count > 0)
            {
                PreCompressedObjectChunks = new List<byte[]>();
                for (int i = 0; i < CustomObjectEntries.Count; i += chunkSize)
                {
                    var chunk = CustomObjectEntries.GetRange(i, Math.Min(chunkSize, CustomObjectEntries.Count - i));
                    PreCompressedObjectChunks.Add(CompressObjectChunk(chunk));
                }
            }
        }

        private static byte[] CompressGroundChunk(List<Shared.resources.CustomGroundEntry> entries)
        {
            using var ms = new MemoryStream();
            using var bw = new Shared.NetworkWriter(ms);
            bw.Write(entries.Count);
            foreach (var entry in entries)
            {
                bw.Write(entry.TypeCode);
                var pixels = entry.DecodedPixels ?? new byte[192];
                bw.Write(pixels, 0, Math.Min(pixels.Length, 192));
                if (pixels.Length < 192) bw.Write(new byte[192 - pixels.Length]);
                // Flags byte: bit 0 = NoWalk
                bw.Write((byte)(entry.NoWalk ? 1 : 0));
                // Blend priority: sbyte (-1 = default/lowest, higher wins at edges)
                bw.Write((sbyte)entry.BlendPriority);
                // Speed multiplier: float (1.0 = normal)
                bw.Write(entry.Speed);
            }
            bw.Flush();
            return Ionic.Zlib.ZlibStream.CompressBuffer(ms.ToArray());
        }

        private static byte[] CompressObjectChunk(List<Shared.resources.CustomObjectEntry> entries)
        {
            using var ms = new MemoryStream();
            using var bw = new Shared.NetworkWriter(ms);
            bw.Write(entries.Count);
            foreach (var entry in entries)
            {
                bw.Write(entry.TypeCode);
                bw.Write(entry.SpriteSize); // 0=blocker(no sprite), 8, 16, or 32
                if (entry.SpriteSize > 0 && entry.DecodedPixels != null)
                {
                    int expectedBytes = entry.SpriteSize * entry.SpriteSize * 3;
                    bw.Write(entry.DecodedPixels, 0, Math.Min(entry.DecodedPixels.Length, expectedBytes));
                    if (entry.DecodedPixels.Length < expectedBytes)
                        bw.Write(new byte[expectedBytes - entry.DecodedPixels.Length]);
                }
                // 0=Object(2D solid), 1=Destructible(3D breakable), 2=Decoration(2D walkable), 3=Wall(3D solid), 4=Blocker(invisible)
                byte classFlag = 0;
                if (entry.ObjectClass == "Destructible") classFlag = 1;
                else if (entry.ObjectClass == "Decoration") classFlag = 2;
                else if (entry.ObjectClass == "Wall") classFlag = 3;
                else if (entry.ObjectClass == "Blocker") classFlag = 4;
                bw.Write(classFlag);
            }
            bw.Flush();
            return Ionic.Zlib.ZlibStream.CompressBuffer(ms.ToArray());
        }

        // BuildCustomObjectXml moved to XmlData.cs (centralized with Register/Unregister)

        public virtual void Init()
        {
        }

        private void InitMap()
        {
            var w = Map.Width;
            var h = Map.Height;

            EnemiesCollision = new CollisionMap<Entity>(0, w, h);
            PlayersCollision = new CollisionMap<Entity>(1, w, h);
            Map.CreateEntities();
        }

        public Entity FindPlayerTarget(Entity host)
        {
            Entity closestObj = null;
            var closestSqDist = double.MaxValue;
            foreach (var obj in host.World.Players.Values)
            {
                if (!(obj as IPlayer).IsVisibleToEnemy())
                    continue;

                var sqDist = obj.SqDistTo(host);
                if (sqDist < closestSqDist)
                {
                    closestSqDist = sqDist;
                    closestObj = obj;
                }
            }
            return closestObj;
        }

        public void ProcessPlayerIO(ref TickTime time)
        {
            foreach (var player in Players.Values)
                player.HandleIO(ref time);
            WorldBranch.HandleIO(ref time);
        }

        public bool Update(ref TickTime time)
        {
            try
            {
                Lifetime += time.ElapsedMsDelta;
                WorldBranch.Update(ref time);
                if (IsPastLifetime(ref time))
                    return true;
                UpdateLogic(ref time);
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"World Tick: {e.Message} \n trace: {e.StackTrace}");
                return false;
            }
        }

        public WorldTimer StartNewTimer(int timeMs, Action<World, TickTime> callback)
        {
            var ret = new WorldTimer(timeMs, callback);
            Timers.Add(ret);
            return ret;
        }

        public WorldTimer StartNewTimer(int timeMs, Func<World, TickTime, bool> callback)
        {
            var ret = new WorldTimer(timeMs, callback);
            Timers.Add(ret);
            return ret;
        }

        protected virtual void UpdateLogic(ref TickTime time)
        {
            foreach (var player in Players.Values)
            {
                player.Tick(ref time);
                if (player.Dead)
                    EntitiesToRemove.Add(player);
            }

            foreach (var sellable in SellableObjects.Values)
            {
                sellable.Tick(ref time);
                if (sellable.Dead)
                    EntitiesToRemove.Add(sellable);
            }

            foreach (var stat in StaticObjects.Values)
            {
                stat.Tick(ref time);
                if (stat.Dead)
                    EntitiesToRemove.Add(stat);
            }

            foreach (var portal in Portals.Values)
            {
                portal.Tick(ref time);
                if (portal.Dead)
                    EntitiesToRemove.Add(portal);
            }

            foreach (var container in Containers.Values)
            {
                container.Tick(ref time);
                if (container.Dead)
                    EntitiesToRemove.Add(container);
            }

            foreach (var pet in Pets.Values)
            {
                pet.Tick(ref time);
                if (pet.Dead)
                    EntitiesToRemove.Add(pet);
            }

            if (EnemiesCollision != null)
            {
                foreach (var entity in EnemiesCollision.GetActiveChunks(PlayersCollision))
                {
                    entity.Tick(ref time);
                    if (entity.Dead)
                        EntitiesToRemove.Add(entity);
                }
            }
            else
            {
                foreach (var entity in Enemies.Values)
                {
                    entity.Tick(ref time);
                    if (entity.Dead)
                        EntitiesToRemove.Add(entity);
                }
            }

            for (var i = Timers.Count - 1; i >= 0; i--)
                try
                {
                    if (Timers[i].Tick(this, ref time))
                        Timers.RemoveAt(i);
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"{e.Message}\n{e.StackTrace}");
                    Timers.RemoveAt(i);
                }

            foreach (var entity in EntitiesToAdd)
                AddToWorld(entity);
            EntitiesToAdd.Clear();

            foreach (var removed in EntitiesToRemove)
                RemoveFromWorld(removed);
            EntitiesToRemove.Clear();

            foreach (var player in Players.Values)
                player.UpdateState(time.ElapsedMsDelta);
        }

        public void FlagForClose()
        {
            _forceLifetimeExpire = true;
        }

        private bool _forceLifetimeExpire = false;

        private bool IsPastLifetime(ref TickTime time)
        {
            if (WorldBranch.HasBranches())
                return false;

            if (Players.Count > 0)
                return false;

            if (_forceLifetimeExpire)
                return true;

            if (Persist)
                return false;

            if (Deleted)
                return false;

            if (Lifetime >= 60000)
                return true;
            return false;
        }

        public void OnRemovedFromWorldManager()
        {
            // Unregister custom objects from shared dictionaries to prevent memory leak
            if (CustomObjectEntries != null && CustomObjectEntries.Count > 0)
            {
                GameServer.Resources.GameData.UnregisterCustomObjects(CustomObjectEntries);
                CustomObjectEntries = null;
            }

            Map.Clear();
            CustomGroundEntries = null;
            CustomDungeonAssetsXml = null;
            PreEncodedDungeonAssetsBytes = null;
            PreCompressedGroundChunks = null;
            PreCompressedObjectChunks = null;
        }
    }
}
