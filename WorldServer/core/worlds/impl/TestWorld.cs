using Shared.resources;
using System.Collections.Generic;
using System.IO;

namespace WorldServer.core.worlds.impl
{
    public sealed class TestWorld : World
    {
        public TestWorld(GameServer gameServer, int id, WorldResource resource)
            : base(gameServer, id, resource)
        {
        }

        public override void Init()
        {
        }

        public void LoadJson(string json)
        {
            var gameData = GameServer.Resources.GameData;
            var wmapData = Shared.terrain.Json2Wmap.Convert(gameData, json,
                out List<CustomGroundEntry> customGrounds,
                out List<CustomObjectEntry> customObjects);

            // Register custom objects BEFORE loading map so Wmap.Load() can resolve names
            if (customObjects != null && customObjects.Count > 0)
            {
                CustomObjectEntries = customObjects;
                gameData.RegisterCustomObjects(customObjects);
            }

            FromWorldMap(new MemoryStream(wmapData));

            // Store custom grounds for binary send to client
            if (customGrounds != null && customGrounds.Count > 0)
                CustomGroundEntries = customGrounds;

            // Pre-compress custom message chunks (reused per client connect)
            PreCompressCustomChunks();
        }
    }
}
