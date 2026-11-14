namespace Oxfall.Core.Net
{
    /// <summary>A survivor asleep on the island, as a player's phone hears of it.</summary>
    public readonly struct SleeperSnap
    {
        /// <summary>Their owner number (what is struck or shot at).</summary>
        public readonly int Owner;
        public readonly string Name;
        public readonly float X, Y, Z, Yaw;

        public SleeperSnap(int owner, string name, float x, float y, float z, float yaw)
        {
            Owner = owner;
            Name = name;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }
    }
}
