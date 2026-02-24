using Microsoft.AspNetCore.Mvc;
using Shared.database;
using Shared.database.account;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shared.utils;

namespace App.Controllers
{
    [ApiController]
    [Route("dungeons")]
    public class DungeonBrowserController : ControllerBase
    {
        private readonly CoreService _core;

        public DungeonBrowserController(CoreService core)
        {
            _core = core;
        }

        [HttpPost("list")]
        public void List()
        {
            var names = GetCommunityDungeonNames();
            var db = _core.Database.Conn;
            var results = new List<object>();

            foreach (var name in names)
            {
                var key = $"dungeon:ratings:{name}";
                var hash = db.HashGetAll(key);
                var likes = 0;
                var difficultySum = 0.0;
                var ratingCount = 0;

                foreach (var entry in hash)
                {
                    if (entry.Name == "likes") int.TryParse(entry.Value, out likes);
                    else if (entry.Name == "difficultySum") double.TryParse(entry.Value, out difficultySum);
                    else if (entry.Name == "ratingCount") int.TryParse(entry.Value, out ratingCount);
                }

                var difficulty = ratingCount > 0 ? Math.Round(difficultySum / ratingCount, 1) : 0.0;

                results.Add(new
                {
                    name,
                    likes,
                    difficulty,
                    ratingCount
                });
            }

            var json = JsonSerializer.Serialize(results);
            Response.ContentType = "application/json";
            Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json));
        }

        [HttpPost("rate")]
        public void Rate(
            [FromForm] string guid,
            [FromForm] string password,
            [FromForm] string dungeonName,
            [FromForm] int difficulty,
            [FromForm] bool liked)
        {
            var status = _core.Database.Verify(guid, password, out DbAccount acc);
            if (status != DbLoginStatus.OK)
            {
                Response.CreateError(status.GetInfo());
                return;
            }

            var names = GetCommunityDungeonNames();
            if (!names.Contains(dungeonName))
            {
                Response.CreateError("Dungeon not found");
                return;
            }

            difficulty = Math.Clamp(difficulty, 0, 10);
            var db = _core.Database.Conn;

            // Check if player already voted
            var voteKey = $"dungeon:vote:{dungeonName}:{acc.AccountId}";
            var ratingsKey = $"dungeon:ratings:{dungeonName}";
            var existingVote = db.HashGetAll(voteKey);

            if (existingVote.Length > 0)
            {
                // Remove old vote from aggregates
                var oldDifficulty = 0;
                var oldLiked = false;
                foreach (var e in existingVote)
                {
                    if (e.Name == "difficulty") int.TryParse(e.Value, out oldDifficulty);
                    else if (e.Name == "liked") bool.TryParse(e.Value, out oldLiked);
                }

                db.HashDecrement(ratingsKey, "difficultySum", oldDifficulty);
                db.HashDecrement(ratingsKey, "ratingCount", 1);
                if (oldLiked)
                    db.HashDecrement(ratingsKey, "likes", 1);
            }

            // Store new vote
            db.HashSet(voteKey, new HashEntry[]
            {
                new HashEntry("difficulty", difficulty),
                new HashEntry("liked", liked.ToString())
            });

            // Update aggregates
            db.HashIncrement(ratingsKey, "difficultySum", difficulty);
            db.HashIncrement(ratingsKey, "ratingCount", 1);
            if (liked)
                db.HashIncrement(ratingsKey, "likes", 1);

            Response.CreateSuccess();
        }

        private List<string> GetCommunityDungeonNames()
        {
            var path = Path.Combine(_core.Resources.ResourcePath, "worlds", "community-dungeons.txt");
            if (!System.IO.File.Exists(path))
                return new List<string>();

            return System.IO.File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();
        }
    }
}
