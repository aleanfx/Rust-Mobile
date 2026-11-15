using System.Collections.Generic;
using Oxfall.Core.Gathering;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Player
{
    /// <summary>
    /// The weak spot of the tree or rock being worked: a pale painted X on the bark, a bright glint in a
    /// rock. It shows after the first blow, on the side facing the player, sitting on the bark or rock
    /// itself (found by casting at the node's mesh); a blow aimed at it takes more
    /// (<see cref="GatherTable.WeakSpotBonus"/>) and the spot moves on.
    /// </summary>
    public sealed class WeakSpot
    {
        /// <summary>How close to the spot the line of the crosshair must pass to count (metres).</summary>
        public const float Tolerance = 0.3f;

        private readonly GameObject _cross;
        private readonly GameObject _glint;
        private readonly Material _glintMaterial;
        private ResourceNodeView _node;
        private Vector3 _point;
        private Vector3 _normal;
        private float _pulse;

        public WeakSpot(Material paint, Material flame, Mesh flameMesh)
        {
            _cross = new GameObject("Weak spot X");
            for (int i = 0; i < 2; i++)
            {
                var stroke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(stroke.GetComponent<Collider>());
                stroke.name = "Stroke";
                stroke.transform.SetParent(_cross.transform, false);
                stroke.transform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
                stroke.transform.localScale = new Vector3(0.34f, 0.055f, 0.02f);
                var r = stroke.GetComponent<MeshRenderer>();
                r.sharedMaterial = paint;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            _cross.SetActive(false);

            _glint = new GameObject("Weak spot glint");
            var filter = _glint.AddComponent<MeshFilter>();
            filter.sharedMesh = flameMesh;
            var renderer = _glint.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (flame != null)
            {
                _glintMaterial = new Material(flame) { name = "Weak spot glint" };
                if (_glintMaterial.HasProperty("_CoreColor")) _glintMaterial.SetColor("_CoreColor", new Color(1f, 0.97f, 0.82f));
                if (_glintMaterial.HasProperty("_TipColor")) _glintMaterial.SetColor("_TipColor", new Color(1f, 0.78f, 0.3f));
                if (_glintMaterial.HasProperty("_Intensity")) _glintMaterial.SetFloat("_Intensity", 2.6f);
                renderer.sharedMaterial = _glintMaterial;
            }
            _glint.SetActive(false);
        }

        /// <summary>The node whose spot shows, if any.</summary>
        public ResourceNodeView Node => _node;

        /// <summary>Where the spot is (world), if one shows.</summary>
        public Vector3? Point => _node != null ? _point : (Vector3?)null;

        /// <summary>
        /// Whether a blow along this ray, landing on <paramref name="node"/>, is on its spot. False
        /// when the spot shows on another node or none.
        /// </summary>
        public bool IsOn(ResourceNodeView node, Ray aim)
        {
            if (node == null || node != _node) return false;
            var toSpot = _point - aim.origin;
            float along = Vector3.Dot(toSpot, aim.direction);
            if (along < 0f) return false;
            return (toSpot - aim.direction * along).magnitude <= Tolerance;
        }

        /// <summary>
        /// After a blow on <paramref name="node"/> from <paramref name="eye"/>: the spot shows there (the
        /// first blow) or moves on (one that found it). Hidden if the node has none or is spent.
        /// </summary>
        public void AfterBlow(ResourceNodeView node, Vector3 eye, bool found)
        {
            if (node == null || !GatherTable.HasWeakSpot(node.Node.Type) || node.Node.IsDepleted)
            {
                Hide();
                return;
            }
            if (node == _node && !found) return;
            for (int attempt = 0; attempt < 6; attempt++)
                if (Place(node, eye)) return;
            Hide();
        }

        public void Hide()
        {
            _node = null;
            _cross.SetActive(false);
            _glint.SetActive(false);
        }

        /// <summary>Keeps the spot honest: gone with its node, or when the player walks away; the glint pulses.</summary>
        public void Tick(Vector3 eye, float dt)
        {
            if (_node == null) return;
            if (_node.Node.IsDepleted || !_node.gameObject.activeInHierarchy || (eye - _point).sqrMagnitude > 10f * 10f)
            {
                Hide();
                return;
            }
            if (_glint.activeSelf)
            {
                _pulse += dt;
                float s = 0.36f + 0.08f * Mathf.Sin(_pulse * 7f);
                _glint.transform.localScale = new Vector3(s, s * 1.1f, s);
            }
        }

        private bool Place(ResourceNodeView node, Vector3 eye)
        {
            var t = node.transform;
            var centre = t.position;
            var away = eye - centre;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = t.forward;
            away.Normalize();
            bool tree = node.Node.Type == ResourceNodeType.Tree;
            // On the side facing the player, swung a little left or right, at a height a blow reaches.
            float turn = Random.Range(-50f, 50f);
            var dir = Quaternion.Euler(0f, turn, 0f) * away;
            float scale = t.lossyScale.y;
            float height = tree ? Random.Range(0.75f, 1.75f) : Random.Range(0.4f, 0.8f) * Mathf.Max(0.6f, scale);
            var target = centre + Vector3.up * height;
            var from = target + dir * 4f;
            var mesh = node.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || !MeshRay.Cast(mesh, 0, t, new Ray(from, -dir), 4.5f, out var hit, out var normal)) return false;
            // Facing the player's side, not the inside of a fork.
            if (Vector3.Dot(normal, dir) < 0.2f) return false;
            _node = node;
            _point = hit + normal * 0.012f;
            _normal = normal;
            if (tree)
            {
                _glint.SetActive(false);
                _cross.SetActive(true);
                _cross.transform.SetPositionAndRotation(_point, Quaternion.LookRotation(-_normal, Vector3.up));
                // Smaller on a thin trunk.
                float trunk = Mathf.Clamp(scale, 0.7f, 1.3f);
                _cross.transform.localScale = Vector3.one * trunk;
            }
            else
            {
                _cross.SetActive(false);
                _glint.SetActive(true);
                _glint.transform.position = _point + _normal * 0.04f;
                _pulse = 0f;
            }
            return true;
        }
    }

    /// <summary>A ray against one submesh of a mesh as drawn by a transform (the shells kept by Remember, or a readable mesh).</summary>
    public static class MeshRay
    {
        private static readonly Dictionary<(Mesh, int), (Vector3[] Vertices, int[] Triangles)> Cache =
            new Dictionary<(Mesh, int), (Vector3[], int[])>();

        /// <summary>Keeps one submesh's triangles for casting at, before the mesh drops its own copy.</summary>
        public static void Remember(Mesh mesh, int submesh, Vector3[] vertices, int[] triangles) =>
            Cache[(mesh, submesh)] = (vertices, triangles);

        public static bool Cast(Mesh mesh, int submesh, Transform t, Ray ray, float maxDistance, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            if (!Cache.TryGetValue((mesh, submesh), out var data))
            {
                // Meshes are sent to the GPU and dropped from memory once built; those cast against
                // keep what is needed (see Remember).
                if (!mesh.isReadable || submesh >= mesh.subMeshCount) return false;
                data = (mesh.vertices, mesh.GetTriangles(submesh));
                Cache[(mesh, submesh)] = data;
            }
            // Into the mesh's own space, where its triangles are.
            var o = t.InverseTransformPoint(ray.origin);
            var d = t.InverseTransformDirection(ray.direction);
            float scaleAlong = d.magnitude;
            if (scaleAlong < 1e-6f) return false;
            d /= scaleAlong;
            float best = maxDistance * scaleAlong * 2f;
            int bestTri = -1;
            var v = data.Vertices;
            var tri = data.Triangles;
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                var a = v[tri[i]];
                var e1 = v[tri[i + 1]] - a;
                var e2 = v[tri[i + 2]] - a;
                var p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (det > -1e-7f && det < 1e-7f) continue;
                float inv = 1f / det;
                var s = o - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                var q = Vector3.Cross(s, e1);
                float w = Vector3.Dot(d, q) * inv;
                if (w < 0f || u + w > 1f) continue;
                float dist = Vector3.Dot(e2, q) * inv;
                if (dist <= 0f || dist >= best) continue;
                best = dist;
                bestTri = i;
            }
            if (bestTri < 0) return false;
            var hitLocal = o + d * best;
            var n = Vector3.Cross(v[tri[bestTri + 1]] - v[tri[bestTri]], v[tri[bestTri + 2]] - v[tri[bestTri]]);
            point = t.TransformPoint(hitLocal);
            normal = t.TransformDirection(n).normalized;
            // Whichever way the triangle winds, the normal faces where the ray came from.
            if (Vector3.Dot(normal, ray.direction) > 0f) normal = -normal;
            return (point - ray.origin).magnitude <= maxDistance;
        }
    }
}
