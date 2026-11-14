using System;
using System.Collections.Generic;
using Oxfall.Core.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Arrows in the air: each flies on its own arc among the real colliders (falling as it goes) and
    /// sticks where it lands for a while, in whatever it hit (moving with an animal or a player). The
    /// shooter's own arrows say what they landed on; other players' only show.
    /// </summary>
    public sealed class ArrowFlights : MonoBehaviour
    {
        private sealed class Flight
        {
            public Transform Arrow;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public bool Stuck;
            public Action<Collider, Vector3> Landed;
        }

        private const float MaxFlightSeconds = 3f;
        private const float StuckSeconds = 8f;
        private const int MaxArrows = 24;

        private readonly List<Flight> _flights = new List<Flight>();
        private Mesh _mesh;
        private Material[] _materials;

        public int Flying
        {
            get
            {
                int n = 0;
                foreach (var f in _flights) if (!f.Stuck) n++;
                return n;
            }
        }

        public static ArrowFlights Create(Mesh mesh, Material[] materials)
        {
            var flights = new GameObject("Arrows").AddComponent<ArrowFlights>();
            flights._mesh = mesh;
            flights._materials = materials;
            return flights;
        }

        /// <summary>
        /// Looses an arrow from <paramref name="origin"/> along <paramref name="direction"/>.
        /// <paramref name="landed"/> hears what it hit (null: nothing, it flew out) and where.
        /// </summary>
        public void Launch(Vector3 origin, Vector3 direction, Action<Collider, Vector3> landed = null)
        {
            if (_flights.Count >= MaxArrows) Remove(0);
            var go = new GameObject("Arrow");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = _materials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            var velocity = direction.normalized * Combat.ArrowSpeed;
            go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(velocity));
            _flights.Add(new Flight { Arrow = go.transform, Position = origin, Velocity = velocity, Landed = landed });
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                f.Age += dt;
                if (f.Arrow == null)
                {
                    _flights.RemoveAt(i);
                    continue;
                }
                if (f.Stuck)
                {
                    if (f.Age > StuckSeconds) Remove(i);
                    continue;
                }
                var next = f.Position + f.Velocity * dt;
                f.Velocity += Vector3.down * Combat.ArrowGravity * dt;
                if (Physics.Linecast(f.Position, next, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    // In up to two thirds of its head, along the way it was flying.
                    var forward = f.Velocity.normalized;
                    f.Arrow.SetPositionAndRotation(hit.point - forward * 0.5f, Quaternion.LookRotation(forward));
                    // Moves with an animal, a player or a sleeper it struck; anything else stays put anyway.
                    if (hit.collider.GetComponent<AnimalTag>() != null || hit.collider.GetComponent<Net.PlayerTag>() != null ||
                        hit.collider.GetComponent<Net.SleeperTag>() != null)
                        f.Arrow.SetParent(hit.collider.transform, true);
                    f.Stuck = true;
                    f.Age = 0f;
                    f.Landed?.Invoke(hit.collider, hit.point);
                    continue;
                }
                f.Position = next;
                f.Arrow.SetPositionAndRotation(next, Quaternion.LookRotation(f.Velocity));
                if (f.Age > MaxFlightSeconds || next.y < -5f)
                {
                    f.Landed?.Invoke(null, next);
                    Remove(i);
                }
            }
        }

        private void Remove(int i)
        {
            var f = _flights[i];
            if (f.Arrow != null) Destroy(f.Arrow.gameObject);
            _flights.RemoveAt(i);
        }
    }
}
