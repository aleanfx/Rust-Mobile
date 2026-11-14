namespace Oxfall.Core.Wildlife
{
    /// <summary>An animal as a player's phone hears of it: enough to draw it and walk it on between updates.</summary>
    public readonly struct AnimalSnap
    {
        public readonly int Id;
        public readonly AnimalKind Kind;
        public readonly AnimalState State;
        public readonly float X, Z;
        public readonly float Yaw;
        public readonly float Speed;

        /// <summary>Health left, 0 to 1.</summary>
        public readonly float Health;

        public AnimalSnap(int id, AnimalKind kind, AnimalState state, float x, float z, float yaw, float speed, float health)
        {
            Id = id;
            Kind = kind;
            State = state;
            X = x;
            Z = z;
            Yaw = yaw;
            Speed = speed;
            Health = health;
        }

        public static AnimalSnap Of(Animal a) =>
            new AnimalSnap(a.Id, a.Kind, a.State, a.X, a.Z, a.Yaw, a.Speed, a.Health / Animals.MaxHealth(a.Kind));
    }

    /// <summary>What a blow at an animal did.</summary>
    public enum AnimalBlow : byte
    {
        /// <summary>Nothing (too far, too soon, gone, or a tool that does no harm).</summary>
        None = 0,
        /// <summary>Hurt it; it lives.</summary>
        Hurt = 1,
        /// <summary>Killed it: a carcass now.</summary>
        Killed = 2,
        /// <summary>Cut meat and leather off its carcass.</summary>
        Cut = 3,
        /// <summary>Cut the last of it: the carcass is gone.</summary>
        UsedUp = 4,
        /// <summary>A carcass, but nothing in hand that cuts.</summary>
        NeedsBlade = 5,
    }
}
