using Shared;
using System;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        internal class Glow : Command
        {
            public override RankingType RankRequirement => RankingType.Supporter;
            public override string CommandName => "glow";

            protected override bool Process(Player player, TickTime time, string color)
            {
                if (string.IsNullOrWhiteSpace(color))
                {
                    player.SendInfo("Usage: /glow <color> \n Number of the color needs to be a HexCode (0xFFFFFF = White, use 0x instahead #), search in google HexCode + Color.");
                    return true;
                }

                player.Glow = Utils.FromString(color);

                var acc = player.Client.Account;
                acc.GlowColor = player.Glow;
                acc.FlushAsync();

                return true;
            }
        }

        internal class Size : Command
        {
            public override RankingType RankRequirement => RankingType.Donator;
            public override string CommandName => "size";

            protected override bool Process(Player player, TickTime time, string args)
            {
                if (string.IsNullOrEmpty(args))
                {
                    player.SendError("Usage: /size <positive integer>. Using 0 will restore the default size for the sprite.");
                    return false;
                }

                var size = Utils.FromString(args);
                var acc = player.Client.Account;
                var rank = acc.Admin ? (int)RankingType.Admin : acc.Rank;

                int min, max;
                if (rank >= (int)RankingType.Admin)
                {
                    min = 20; max = 250;
                }
                else if (rank >= (int)RankingType.Sponsor) // Skeleking
                {
                    min = 25; max = 225;
                }
                else if (rank >= (int)RankingType.Supporter) // Skeleguard
                {
                    min = 50; max = 175;
                }
                else // Skelenoid (Donator)
                {
                    min = 75; max = 125;
                }

                if (size < min && size != 0 || size > max)
                {
                    player.SendError($"Invalid size. Your rank allows: {min}-{max}. Use 0 to reset size to default.");
                    return false;
                }

                acc.Size = size;
                acc.FlushAsync();

                if (size == 0)
                    player.Size = 100;
                else
                    player.Size = size;

                return true;
            }
        }
    }
}
