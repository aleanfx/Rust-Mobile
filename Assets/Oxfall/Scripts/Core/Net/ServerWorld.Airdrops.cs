using System.Collections.Generic;
using Oxfall.Core.Gathering;
using Oxfall.Core.World;

namespace Oxfall.Core.Net
{
    /// <summary>Supply drops on the server: a plane now and then, everyone told, the crate set down where it lands.</summary>
    public sealed partial class ServerWorld
    {
        private readonly List<Airdrop> _landed = new List<Airdrop>();

        public Airdrops Airdrops { get; private set; }

        /// <summary>Planes that set off since the last tick was sent (for everyone to see).</summary>
        public List<Airdrop> AirdropsStarted { get; } = new List<Airdrop>();

        private void CreateAirdrops(int seed, Obstacles obstacles)
        {
            Airdrops = new Airdrops(Ground, seed)
            {
                // Not onto a trunk, a rock or anyone's building.
                Blocked = (x, z) => obstacles.Blocks(x, z, 2f) || Settlement.Grid.FoundationNear(x, z, 4.5f),
            };
        }

        private void TickAirdrops()
        {
            Airdrops.Tick(Time, AirdropsStarted, _landed);
            foreach (var drop in _landed)
                Settlement.LandSupplyDrop(drop.DropX, drop.GroundY, drop.DropZ, drop.Yaw, LootTable.SupplyDrop(_random));
            _landed.Clear();
        }
    }
}
