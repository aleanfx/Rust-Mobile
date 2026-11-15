using System;
using System.Collections.Generic;
using Oxfall.Core.Rules;
using Oxfall.Game.Items;
using Oxfall.Game.Rendering;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Construction
{
    /// <summary>Marks the collider of something set down, so aiming at it finds which one it is.</summary>
    public sealed class DeployableTag : MonoBehaviour
    {
        public int Id;
        public DeployableKind Kind;
    }

    /// <summary>A recycler's moving parts: its flywheel spins up while it runs and runs down when it stops.</summary>
    public sealed class RecyclerView : MonoBehaviour
    {
        public Transform Wheel;
        /// <summary>Where its hum plays from, while it runs.</summary>
        public GameObject Hum;
        public bool Running;
        private float _speed;

        private void Update()
        {
            _speed = Mathf.MoveTowards(_speed, Running ? 560f : 0f, 700f * Time.deltaTime);
            if (_speed > 0f && Wheel != null) Wheel.Rotate(_speed * Time.deltaTime, 0f, 0f, Space.Self);
        }
    }

    /// <summary>An automatic turret's head: it sweeps slowly while it has nothing to shoot at, and snaps round onto a target.</summary>
    public sealed class TurretView : MonoBehaviour
    {
        public Transform Head;
        private float _yaw, _pitch, _aimYaw, _aimPitch, _aimedAt = -10f;

        /// <summary>Whether power reaches it: without, its head hangs and it does not look about.</summary>
        public bool Powered = true;

        /// <summary>Turns the head to a point in the world (it holds that aim a while).</summary>
        public void Aim(Vector3 at)
        {
            var local = transform.InverseTransformPoint(at) - MeshFactory.TurretPivot;
            _aimYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            _aimPitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            _aimedAt = Time.time;
        }

        /// <summary>Where the barrel's end is now, and the way it points.</summary>
        public void Muzzle(out Vector3 at, out Vector3 forward)
        {
            forward = Head.forward;
            at = Head.TransformPoint(new Vector3(0.05f, 0.12f, 0.78f));
        }

        private void Update()
        {
            if (Head == null) return;
            bool aiming = Time.time - _aimedAt < 2.5f;
            float wantYaw = aiming ? _aimYaw : Powered ? Mathf.Sin(Time.time * 0.45f + transform.position.x) * 60f : _yaw;
            float wantPitch = aiming ? _aimPitch : Powered ? 0f : 30f;
            float rate = aiming ? 540f : 40f;
            _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, rate * Time.deltaTime);
            _pitch = Mathf.MoveTowardsAngle(_pitch, Mathf.Clamp(wantPitch, -35f, 45f), rate * Time.deltaTime);
            Head.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }

    /// <summary>
    /// Draws what is set down in the settlement and nothing else: it listens to the Core
    /// <see cref="Settlement"/> and adds, lights and removes objects to match. A campfire always
    /// burns; a furnace burns while it is lit.
    /// </summary>
    public sealed class DeployableViews
    {
        private readonly Settlement _settlement;
        private readonly Dictionary<DeployableKind, ItemModel> _models = new Dictionary<DeployableKind, ItemModel>();
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Fire> _fires = new Dictionary<int, Fire>();
        private readonly Mesh _flame;
        private readonly Material _flameMaterial;
        private readonly Transform _root;

        /// <summary>A fire started (a campfire set down, a furnace lit), for its crackle.</summary>
        public event Action<GameObject> FireStarted;

        /// <summary>A recycler started: the object its hum should play from.</summary>
        public event Action<GameObject> MachineStarted;

        private readonly Mesh _hempPlant, _berryPlant;
        private readonly Mesh _turretBase = MeshFactory.AutoTurretBase(), _turretHead = MeshFactory.AutoTurretHead();
        private readonly ItemModel _soil;
        private readonly Material[] _hempMaterials, _berryMaterials, _greenBerryMaterials, _witheredMaterials;
        private readonly Dictionary<int, Transform> _plants = new Dictionary<int, Transform>();
        private readonly Mesh _wheel;
        private readonly Material[] _wheelMaterials;
        private readonly Material _lampOn, _lampOff;
        private readonly Material _bulbOn, _bulbOff, _ledDark;
        private readonly Mesh _switchLever = MeshFactory.SwitchLever();
        private readonly Dictionary<int, Light> _lamps = new Dictionary<int, Light>();
        private readonly Dictionary<int, Transform> _levers = new Dictionary<int, Transform>();
        private readonly Dictionary<int, GameObject> _hums = new Dictionary<int, GameObject>();

        /// <summary>Something set down is going (picked up, or destroyed): where it stood and what it was made of.</summary>
        public event Action<Bounds, Material> Fell;

        public int Count => _views.Count;

        /// <summary>The player's body, which passes through ladders.</summary>
        public static Collider Walker { get; set; }

        public DeployableViews(Settlement settlement, IReadOnlyDictionary<string, ItemModel> models, MaterialLibrary materials)
        {
            _settlement = settlement;
            foreach (DeployableKind kind in Enum.GetValues(typeof(DeployableKind)))
            {
                string item = Deployables.ItemOf(kind);
                if (item != null && models.TryGetValue(item, out var model)) _models[kind] = model;
            }
            // A dead player's bag is made from no item, so it has a model of its own.
            _models[DeployableKind.Bag] = new ItemModel { Mesh = MeshFactory.LootBag(), Materials = new[] { materials.Cloth, materials.Rope } };
            // So is the ruins' recycler.
            _models[DeployableKind.Recycler] = new ItemModel
            {
                Mesh = MeshFactory.Recycler(),
                Materials = new[] { materials.RecyclerPaint, materials.Steel, materials.BarrelRust, materials.LedRed },
            };
            // And a supply crate, come down from a plane.
            _models[DeployableKind.SupplyDrop] = new ItemModel
            {
                Mesh = MeshFactory.AirdropCrate(),
                Materials = new[] { materials.DropPaint, materials.Steel, materials.ChuteOrange, materials.WhitePaint },
            };
            // Crops: the plant as it will grow (the ghost shows it full size), on a mound of dug earth.
            var shapes = new System.Random(71);
            _hempPlant = MeshFactory.Hemp(shapes);
            _berryPlant = MeshFactory.BerryBush(shapes, false);
            _soil = new ItemModel { Mesh = MeshFactory.SoilMound(), Materials = new[] { materials.Soil } };
            _models[DeployableKind.HempPlant] = new ItemModel { Mesh = _hempPlant, Materials = new[] { materials.Hemp } };
            _models[DeployableKind.BerryPlant] = new ItemModel { Mesh = _berryPlant, Materials = new[] { materials.Berries, materials.Leaves[0] } };
            _hempMaterials = new[] { materials.Hemp };
            _berryMaterials = new[] { materials.Berries, materials.Leaves[0] };
            _greenBerryMaterials = new[] { materials.GreenBerries, materials.Leaves[0] };
            _witheredMaterials = new[] { materials.Withered, materials.Withered };
            _wheel = MeshFactory.RecyclerWheel();
            _wheelMaterials = new[] { materials.Steel, materials.BarrelRust };
            _lampOn = materials.LedGreen;
            _lampOff = materials.LedRed;
            _bulbOn = materials.BulbOn;
            _bulbOff = materials.BulbOff;
            _ledDark = materials.Rubber;
            // The switch in the world is its post, with a lever of its own that turns.
            if (_models.TryGetValue(DeployableKind.ElectricSwitch, out var whole))
                _models[DeployableKind.ElectricSwitch] = new ItemModel { Mesh = MeshFactory.ElectricSwitch(), Materials = new[] { whole.Materials[0], whole.Materials[1] } };
            _switchGrip = whole != null ? new[] { whole.Materials[0], whole.Materials[2] } : null;
            settlement.Power.Changed += PowerChanged;
            _flame = MeshFactory.Flame();
            _flameMaterial = materials.Flame;
            _root = new GameObject("Deployables").transform;
            settlement.DeployableAdded += Show;
            settlement.DeployableRemoved += Hide;
            settlement.DeployableChanged += Changed;
            // Blackened by blasts, the more the closer it is to going.
            settlement.DeployableHealthChanged += (id, health) =>
            {
                if (!_views.TryGetValue(id, out var go) || !_settlement.TryGet(id, out var d)) return;
                BuildingViews.Scorch(go.GetComponent<MeshRenderer>(), health / Mathf.Max(1f, Deployables.MaxHealth(d.Kind)));
            };
        }

        private readonly Material[] _switchGrip;

        public ItemModel ModelOf(DeployableKind kind) => _models.TryGetValue(kind, out var m) ? m : null;

        public bool TryGetView(int id, out GameObject view) => _views.TryGetValue(id, out view);

        private void Show(Deployable d)
        {
            if (_views.ContainsKey(d.Id) || !_models.TryGetValue(d.Kind, out var model)) return;
            if (Crops.IsCrop(d.Kind))
            {
                ShowCrop(d);
                return;
            }
            if (d.Kind == DeployableKind.AutoTurret)
            {
                ShowTurret(d, model);
                return;
            }
            var go = new GameObject(d.Kind.ToString());
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(new Vector3(d.X, d.Y, d.Z), Quaternion.Euler(0f, d.Yaw, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = model.Materials;
            var collider = go.AddComponent<BoxCollider>();
            var bounds = model.Mesh.bounds;
            collider.center = bounds.center;
            // At least knee high, so it is easy to aim at (a sleeping bag lies flat).
            collider.size = new Vector3(bounds.size.x, Mathf.Max(bounds.size.y, 0.35f), bounds.size.z);
            if (HighWalls.IsWall(d.Kind))
            {
                // Just the wall itself (not the braces behind a wooden one): a slab no one walks or shoots through.
                float low = -HighWalls.Sunk, high = HighWalls.Height;
                collider.center = new Vector3(0f, (low + high) * 0.5f, 0f);
                collider.size = new Vector3(HighWalls.Length, high - low, HighWalls.Thickness);
            }
            // A ladder is climbed, not walked into: the player passes through it (aiming still finds it).
            else if (d.Kind == DeployableKind.Ladder && Walker != null) Physics.IgnoreCollision(collider, Walker);
            var tag = go.AddComponent<DeployableTag>();
            tag.Id = d.Id;
            tag.Kind = d.Kind;
            _views.Add(d.Id, go);
            float max = Deployables.MaxHealth(d.Kind);
            if (max > 0f && _settlement.DeployableHealth(d.Id) < max)
                BuildingViews.Scorch(go.GetComponent<MeshRenderer>(), _settlement.DeployableHealth(d.Id) / max);
            if (d.Kind == DeployableKind.Campfire)
                Light(d.Id, go, new Vector3(0f, 0.04f, 0f), 1.7f, 5, 14f, 3.4f);
            else if (d.Kind == DeployableKind.Furnace && _settlement.IsLit(d.Id))
                Changed(d.Id);
            else if (Electricity.IsElectric(d.Kind))
                Electrify(d, go);
            else if (d.Kind == DeployableKind.Recycler)
            {
                var wheel = new GameObject("Flywheel");
                wheel.transform.SetParent(go.transform, false);
                wheel.transform.localPosition = new Vector3(-0.99f, 0.62f, -0.12f);
                wheel.AddComponent<MeshFilter>().sharedMesh = _wheel;
                wheel.AddComponent<MeshRenderer>().sharedMaterials = _wheelMaterials;
                go.AddComponent<RecyclerView>().Wheel = wheel.transform;
                Changed(d.Id);
            }
        }

        /// <summary>An automatic turret: its stand, and a head on it that turns.</summary>
        private void ShowTurret(Deployable d, ItemModel model)
        {
            var go = new GameObject(d.Kind.ToString());
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(new Vector3(d.X, d.Y, d.Z), Quaternion.Euler(0f, d.Yaw, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = _turretBase;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { model.Materials[0], model.Materials[1], model.Materials[2] };
            var head = new GameObject("Head");
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = MeshFactory.TurretPivot;
            head.AddComponent<MeshFilter>().sharedMesh = _turretHead;
            head.AddComponent<MeshRenderer>().sharedMaterials = model.Materials;
            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.55f, 0f);
            collider.size = new Vector3(0.7f, 1.1f, 0.7f);
            var tag = go.AddComponent<DeployableTag>();
            tag.Id = d.Id;
            tag.Kind = d.Kind;
            go.AddComponent<TurretView>().Head = head.transform;
            _views.Add(d.Id, go);
            PowerChanged(d.Id);
            float max = Deployables.MaxHealth(d.Kind);
            if (_settlement.DeployableHealth(d.Id) < max)
                BuildingViews.Scorch(go.GetComponent<MeshRenderer>(), _settlement.DeployableHealth(d.Id) / max);
        }

        /// <summary>A turret's head and barrel, to aim it and fire from it (null: no such turret on screen).</summary>
        public TurretView Turret(int id) => _views.TryGetValue(id, out var go) ? go.GetComponent<TurretView>() : null;

        /// <summary>A sown crop: a mound of earth with the plant on it, as big as it has grown.</summary>
        private void ShowCrop(Deployable d)
        {
            var go = new GameObject(d.Kind.ToString());
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(new Vector3(d.X, d.Y, d.Z), Quaternion.Euler(0f, d.Yaw, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = _soil.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = _soil.Materials;
            var plant = new GameObject("Plant");
            plant.transform.SetParent(go.transform, false);
            plant.AddComponent<MeshFilter>().sharedMesh = d.Kind == DeployableKind.BerryPlant ? _berryPlant : _hempPlant;
            plant.AddComponent<MeshRenderer>();
            // Easy to aim at whatever its size; knee high, so it is looked at, not walked into.
            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.45f, 0f);
            collider.size = new Vector3(0.5f, 0.9f, 0.5f);
            if (Walker != null) Physics.IgnoreCollision(collider, Walker);
            var tag = go.AddComponent<DeployableTag>();
            tag.Id = d.Id;
            tag.Kind = d.Kind;
            _views.Add(d.Id, go);
            _plants[d.Id] = plant.transform;
            Grow(d.Id, d.Kind);
        }

        /// <summary>A crop's plant drawn for the stage it is at: bigger as it grows, berries ripening, brown once withered.</summary>
        private void Grow(int id, DeployableKind kind)
        {
            if (!_plants.TryGetValue(id, out var plant)) return;
            var stage = _settlement.CropStageOf(id);
            float size;
            switch (stage)
            {
                case CropStage.Sprout: size = 0.22f; break;
                case CropStage.Young: size = 0.5f; break;
                case CropStage.Grown: size = 0.8f; break;
                case CropStage.Ripe: size = 1f; break;
                default: size = 0.85f; break;
            }
            bool berry = kind == DeployableKind.BerryPlant;
            plant.localScale = new Vector3(size, stage == CropStage.Withered ? size * 0.7f : size, size) * (berry ? 1.1f : 1f);
            plant.localRotation = stage == CropStage.Withered ? Quaternion.Euler(12f, 0f, 8f) : Quaternion.identity;
            plant.GetComponent<MeshRenderer>().sharedMaterials = stage == CropStage.Withered ? (berry ? _witheredMaterials : new[] { _witheredMaterials[0] })
                : !berry ? _hempMaterials
                : stage == CropStage.Ripe ? _berryMaterials : _greenBerryMaterials;
        }

        /// <summary>The working parts of an electric thing: a lamp's light, a switch's lever.</summary>
        private void Electrify(Deployable d, GameObject go)
        {
            if (d.Kind == DeployableKind.Lamp)
            {
                var lightObject = new GameObject("Lamp light");
                lightObject.transform.SetParent(go.transform, false);
                lightObject.transform.localPosition = MeshFactory.LampBulb - new Vector3(0f, 0.08f, 0f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.87f, 0.64f);
                light.range = 11f;
                light.intensity = 2.6f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                _lamps[d.Id] = light;
            }
            else if (d.Kind == DeployableKind.ElectricSwitch && _switchGrip != null)
            {
                var lever = new GameObject("Lever");
                lever.transform.SetParent(go.transform, false);
                lever.transform.localPosition = MeshFactory.SwitchPivot;
                lever.AddComponent<MeshFilter>().sharedMesh = _switchLever;
                lever.AddComponent<MeshRenderer>().sharedMaterials = _switchGrip;
                _levers[d.Id] = lever.transform;
            }
            PowerChanged(d.Id);
        }

        /// <summary>Power reached or left a device, a switch flipped, a battery filled: show it.</summary>
        private void PowerChanged(int id)
        {
            if (!_views.TryGetValue(id, out var go) || !_settlement.TryGet(id, out var d)) return;
            var grid = _settlement.Power;
            bool running = grid.Running(id);
            switch (d.Kind)
            {
                case DeployableKind.Lamp:
                    SetSlot(go, 2, running ? _bulbOn : _bulbOff);
                    if (_lamps.TryGetValue(id, out var light)) light.enabled = running;
                    break;
                case DeployableKind.ElectricSwitch:
                    if (_levers.TryGetValue(id, out var lever))
                        lever.localRotation = Quaternion.Euler(grid.IsOn(id) ? 0f : 120f, 0f, 0f);
                    break;
                case DeployableKind.Generator:
                    SetSlot(go, 3, running ? _lampOn : _lampOff);
                    Hum(id, go, running);
                    break;
                case DeployableKind.Battery:
                {
                    float charge = grid.Charge01(id);
                    SetSlot(go, 3, charge > 0.02f ? _lampOn : _lampOff);
                    SetSlot(go, 4, charge > 0.4f ? _lampOn : _ledDark);
                    SetSlot(go, 5, charge > 0.75f ? _lampOn : _ledDark);
                    break;
                }
                case DeployableKind.AutoTurret:
                {
                    var turret = go.GetComponent<TurretView>();
                    if (turret != null) turret.Powered = running;
                    break;
                }
            }
        }

        private static void SetSlot(GameObject go, int slot, Material material)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            var materials = renderer.sharedMaterials;
            if (slot >= materials.Length || materials[slot] == material) return;
            materials[slot] = material;
            renderer.sharedMaterials = materials;
        }

        /// <summary>A generator hums while it runs.</summary>
        private void Hum(int id, GameObject go, bool on)
        {
            bool humming = _hums.TryGetValue(id, out var hum) && hum != null;
            if (on == humming) return;
            if (on)
            {
                hum = new GameObject("Hum");
                hum.transform.SetParent(go.transform, false);
                hum.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                _hums[id] = hum;
                MachineStarted?.Invoke(hum);
            }
            else
            {
                UnityEngine.Object.Destroy(hum);
                _hums.Remove(id);
            }
        }

        private void Hide(int id)
        {
            _plants.Remove(id);
            _lamps.Remove(id);
            _levers.Remove(id);
            _hums.Remove(id);
            if (!_views.TryGetValue(id, out var go)) return;
            _views.Remove(id);
            _fires.Remove(id);
            var renderer = go.GetComponent<MeshRenderer>();
            // (A recycler only goes when the whole island is redrawn: no dust for it.)
            if (renderer != null && go.GetComponent<RecyclerView>() == null) Fell?.Invoke(renderer.bounds, renderer.sharedMaterial);
            UnityEngine.Object.Destroy(go);
        }

        /// <summary>A furnace lit up or went out; a recycler started or stopped.</summary>
        private void Changed(int id)
        {
            if (!_views.TryGetValue(id, out var go) || !_settlement.TryGet(id, out var d)) return;
            if (Crops.IsCrop(d.Kind))
            {
                Grow(id, d.Kind);
                return;
            }
            if (d.Kind == DeployableKind.Recycler)
            {
                Run(go, _settlement.IsLit(id));
                return;
            }
            if (d.Kind != DeployableKind.Furnace) return;
            bool lit = _settlement.IsLit(id);
            if (lit && !_fires.ContainsKey(id))
                Light(id, go, new Vector3(0f, 0.16f, 0.42f), 0.9f, 3, 9f, 2.6f);
            else if (!lit && _fires.TryGetValue(id, out var fire))
            {
                _fires.Remove(id);
                UnityEngine.Object.Destroy(fire.gameObject);
            }
        }

        /// <summary>A recycler's lamp goes green and its flywheel turns, humming, while it runs; red and still when it stops.</summary>
        private void Run(GameObject go, bool running)
        {
            var view = go.GetComponent<RecyclerView>();
            if (view == null) return;
            var renderer = go.GetComponent<MeshRenderer>();
            var materials = renderer.sharedMaterials;
            materials[3] = running ? _lampOn : _lampOff;
            renderer.sharedMaterials = materials;
            if (view.Running == running) return;
            view.Running = running;
            if (running)
            {
                view.Hum = new GameObject("Hum");
                view.Hum.transform.SetParent(go.transform, false);
                view.Hum.transform.localPosition = new Vector3(0f, 0.8f, 0f);
                MachineStarted?.Invoke(view.Hum);
            }
            else if (view.Hum != null)
            {
                UnityEngine.Object.Destroy(view.Hum);
                view.Hum = null;
            }
        }

        private void Light(int id, GameObject go, Vector3 at, float scale, int flames, float range, float intensity)
        {
            var fire = Fire.Create(go.transform, at, scale, flames, range, intensity, _flame, _flameMaterial);
            _fires[id] = fire;
            FireStarted?.Invoke(fire.gameObject);
        }
    }
}
