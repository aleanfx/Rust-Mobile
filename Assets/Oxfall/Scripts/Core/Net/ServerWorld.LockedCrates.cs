using Oxfall.Core.Gathering;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>Locked crates on the server: one now and then in an outpost's yard, the guards woken when someone hacks it.</summary>
    public sealed partial class ServerWorld
    {
        public LockedCrates LockedCrates { get; private set; }

        private void CreateLockedCrates(int seed) => LockedCrates = new LockedCrates(seed);

        /// <summary>Whether a locked crate is on the island now.</summary>
        public bool LockedCratePresent
        {
            get
            {
                foreach (var d in Settlement.Deployables)
                    if (d.Kind == DeployableKind.LockedCrate) return true;
                return false;
            }
        }

        private void TickLockedCrates()
        {
            if (LockedCrates.Due(Time, LockedCratePresent)) SpawnLockedCrate();
        }

        /// <summary>A locked crate now, in an outpost's yard (everyone sees it come through LootDropped). Null if the island has no ruins.</summary>
        public Deployable? SpawnLockedCrate()
        {
            if (!LockedCrates.PickSpot(Ground, out float x, out float y, out float z, out float yaw)) return null;
            return Settlement.PlaceLockedCrate(x, y, z, yaw, LootTable.LockedCrate(_random));
        }

        /// <summary>Someone started hacking a crate: the guards round it come for them.</summary>
        private void HackStarted(ushort playerId, int crate)
        {
            if (!Settlement.TryGet(crate, out var d)) return;
            Fauna.CallBandits(d.X, d.Z, LockedCrates.AlarmRadius, playerId);
        }
    }
}
