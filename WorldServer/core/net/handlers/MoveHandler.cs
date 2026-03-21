using Shared;
using System;
using System.Collections.Generic;
using WorldServer.networking;
using WorldServer.utils;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.net.datas;
using System.Runtime.CompilerServices;

namespace WorldServer.core.net.handlers
{
    public class MoveHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MOVE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var tickId = rdr.ReadInt32();
            var time = rdr.ReadInt32();
            var newX = rdr.ReadSingle();
            var newY = rdr.ReadSingle();

            var len = rdr.ReadInt16();
            if(len < 0 || len >= 16)
            {
                client.Disconnect("Invalid Records");
                return;
            }

            var moveRecords = new TimedPosition[len];
            for (var i = 0; i < moveRecords.Length; i++)
                moveRecords[i] = TimedPosition.Read(rdr);

            var player = client.Player;

            player.HandleProjectileDetection(time, newX, newY, ref moveRecords);

            // Riding: redirect movement to the mount
            if (player.IsRiding)
            {
                var mount = player.GetRidingEnemy();
                if (mount == null || mount.Dead)
                {
                    player.Dismount();
                    player.SendInfo("Your mount is gone!");
                }
                else if (mount.ObjectDesc.Raft)
                {
                    // Raft: player sends desired world position, server clamps to raft bounds
                    // 3x4 tile raft, bottom-center anchored
                    var offsetX = Math.Clamp(newX - mount.X, -1.5f, 1.5f);
                    var offsetY = Math.Clamp(newY - mount.Y, -3.0f, 0.0f);
                    var finalX = mount.X + offsetX;
                    var finalY = mount.Y + offsetY;

                    // Don't let player walk to a wall tile on the raft
                    if (player.World.IsPassable(finalX, finalY))
                    {
                        player.RaftOffsetX = offsetX;
                        player.RaftOffsetY = offsetY;
                        player.Move(finalX, finalY);
                    }
                    else
                    {
                        // Keep previous offset, re-anchor to raft
                        player.Move(mount.X + player.RaftOffsetX, mount.Y + player.RaftOffsetY);
                    }
                    player.UpdateTiles();
                }
                else if (newX != -1 && newY != -1 && player.World.Map.Contains(newX, newY))
                {
                    // Regular mount: player controls mount position
                    mount.Move(newX, newY);
                    player.Move(newX, newY);
                    player.UpdateTiles();
                }
                player.MoveReceived(tickTime, time, tickId);
                return;
            }

            if (newX != -1 && newX != player.X || newY != -1 && newY != player.Y)
            {
                if (!player.World.Map.Contains(newX, newY))
                {
                    // Don't disconnect — reject the move (MapInfo sends square size, actual map may not be square)
                    player.UpdateTiles();
                    return;
                }

                if (!player.World.IsPassable(newX, newY))
                {
                    // Don't disconnect — just reject the move and keep player at current position
                    // Custom dungeon NoWalk tiles should block, not kick
                    player.UpdateTiles();
                    return;
                }

                if(!player.Client.Account.Admin)
                    if (player.Stars <= 2 && player.Quest != null && player.DistTo(newX, newY) > 50 && player.Quest.DistTo(newX, newY) < 0.25)
                    {
                        StaticLogger.Instance.Warn($"{player.Name} was caught teleporting directly to a quest, uh oh");
                        player.Client.Disconnect("Unexpected Error Occured");
                        return;
                    }

                player.Move(newX, newY);
                player.UpdateTiles();
            }

            player.MoveReceived(tickTime, time, tickId);
        }
    }
}
