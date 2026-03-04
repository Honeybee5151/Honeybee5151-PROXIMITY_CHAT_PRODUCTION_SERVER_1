using System.Xml.Linq;
using Shared;

namespace Shared.resources
{
    public class SkinDesc
    {
        public readonly int Cost;
        public readonly ushort PlayerClassType;
        public readonly int Size;
        public readonly ushort Type;
        public readonly int UnlockLevel;
        public readonly int RequiredRank; //editor8182381 — donation rank required (0=none)

        public string ObjectId;

        public SkinDesc(ushort type, XElement e)
        {
            Type = type;
            ObjectId = e.GetAttribute<string>("id");
            PlayerClassType = e.GetValue<ushort>("PlayerClassType");
            UnlockLevel = e.GetValue<int>("UnlockLevel");
            Cost = e.GetValue("Cost", 300);
            Size = e.GetValue("Size", 100);
            RequiredRank = e.GetValue("RequiredRank", 0); //editor8182381
        }
    }
}
