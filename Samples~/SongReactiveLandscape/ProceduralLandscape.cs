using System;
using System.Collections.Generic;
using UnityEngine;

namespace DanceFlow.Unity.Samples
{
    /// <summary>
    /// Deterministic runtime mesh authoring. Only a few renderers are created: one combined
    /// grass mesh, one ground mesh, two meshes per tree and one warped ribbon sculpture.
    /// No premade tree/grass textures, models or song assets are required.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralLandscape : MonoBehaviour
    {
        [SerializeField, Range(1, 40000)] private int grassBlades = 3000;
        [SerializeField, Range(1, 100)] private int treeCount = 14;
        [SerializeField, Range(10, 150)] private float worldWidth = 70;
        [SerializeField, Range(8, 128)] private int terrainSegments = 64;
        [SerializeField] private int seed = 4162;
        [SerializeField] private Shader landscapeShader;
        [SerializeField] private bool includeWarpSculpture = true;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material> _materials = new List<Material>();
        private Transform _generated;

        private void Start()
        {
            if (!Application.isPlaying) return;
            Build();
        }

        public void Build()
        {
            Clear();
            if (landscapeShader == null) landscapeShader = Shader.Find("DanceFlow/URP/MotionLandscape");
            if (landscapeShader == null || !landscapeShader.isSupported)
            {
                Debug.LogError("Import the Song Reactive Landscape sample and enable URP before building.", this);
                return;
            }
            var parent = new GameObject("Generated DanceFlow landscape");
            parent.transform.SetParent(transform, false);
            _generated = parent.transform;
            var ground = NewMaterial("Ground", new Color(0.10f, 0.20f, 0.25f), 0, 0, 0.55f, 0.20f);
            var grass = NewMaterial("Grass", new Color(0.12f, 0.40f, 0.32f), 1, 0.65f, 0.35f, 0.12f);
            var bark = NewMaterial("Bark", new Color(0.35f, 0.21f, 0.29f), 2, 0.28f, 0.18f, 0.04f);
            var leaves = NewMaterial("Canopy", new Color(0.27f, 0.65f, 0.63f), 3, 1.10f, 0.38f, 0.26f);
            var warp = NewMaterial("Warp sculpture", new Color(0.65f, 0.21f, 0.92f), 4, 0.4f, 1.5f, 0.75f);
            var stars = NewMaterial("Starfield", new Color(0.82f, 0.87f, 1f), 5, 0, 0, 0.24f);
            AddObject("Rolling ground", BuildTerrain(terrainSegments), ground, Vector3.zero);
            AddObject("Batched grass blades", BuildGrass(Math.Min(grassBlades, 40000)), grass, Vector3.zero);
            AddObject("Sky stars", BuildStars(190), stars, Vector3.zero);
            var random = new System.Random(seed);
            for (int i = 0; i < treeCount; i++)
            {
                float x = Range(random, -worldWidth * 0.43f, worldWidth * 0.43f);
                float z = Range(random, -worldWidth * 0.43f, worldWidth * 0.43f);
                // Keep the sculpture clear of the tree canopy.
                if (Mathf.Abs(x) < 8 && Mathf.Abs(z) < 8) { x += Mathf.Sign(x == 0 ? 1 : x) * 9; }
                float height = Range(random, 4.7f, 8.4f);
                float radius = Range(random, 0.5f, 1.2f);
                Vector3 basePosition = new Vector3(x, GroundHeight(x, z), z);
                AddTree(random, i, basePosition, height, radius, bark, leaves);
            }
            if (includeWarpSculpture)
                AddObject("Geometry warp helix", BuildWarpTower(), warp, new Vector3(0, GroundHeight(0, 0) + 0.3f, 0));
        }

        private Material NewMaterial(string name, Color color, float mode, float wind, float warp, float palette)
        {
            var material = new Material(landscapeShader) { name = "DanceFlow " + name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Mode", mode);
            material.SetFloat("_WindGain", wind);
            material.SetFloat("_WarpGain", warp);
            material.SetFloat("_PaletteGain", palette);
            _materials.Add(material);
            return material;
        }
        private static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
        private float GroundHeight(float x, float z)
        {
            float a = Mathf.PerlinNoise(x * 0.055f + 30.5f + seed, z * 0.055f + 81.1f);
            float b = Mathf.PerlinNoise(x * 0.15f + seed * 0.4f, z * 0.15f + 42.3f);
            return (a - 0.5f) * 4.3f + (b - 0.5f) * 0.9f;
        }

        private static Mesh FromLists(string name, List<Vector3> v, List<int> tri,
                                      List<Vector2> uv2 = null, List<Color> colours = null)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            if (uv2 != null) mesh.SetUVs(1, uv2);
            if (colours == null)
            {
                colours = new List<Color>(v.Count);
                for (int i = 0; i < v.Count; i++) colours.Add(Color.white);
            }
            mesh.SetColors(colours);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildTerrain(int segments)
        {
            var v = new List<Vector3>((segments + 1) * (segments + 1));
            var tri = new List<int>(segments * segments * 6);
            var uv = new List<Vector2>(v.Capacity);
            var colors = new List<Color>(v.Capacity);
            for (int z = 0; z <= segments; z++)
            for (int x = 0; x <= segments; x++)
            {
                float xx = worldWidth * (x / (float)segments - 0.5f);
                float zz = worldWidth * (z / (float)segments - 0.5f);
                float h = GroundHeight(xx, zz);
                v.Add(new Vector3(xx, h, zz));
                uv.Add(Vector2.zero);
                colors.Add(Color.Lerp(new Color(0.60f, 0.78f, 0.64f), Color.white, Mathf.InverseLerp(-2f, 2f, h)));
            }
            for (int z = 0; z < segments; z++)
            for (int x = 0; x < segments; x++)
            {
                int i = z * (segments + 1) + x;
                tri.Add(i); tri.Add(i + segments + 1); tri.Add(i + 1);
                tri.Add(i + 1); tri.Add(i + segments + 1); tri.Add(i + segments + 2);
            }
            return FromLists("terrain", v, tri, uv, colors);
        }

        private Mesh BuildGrass(int blades)
        {
            var rng = new System.Random(seed + 17);
            var v = new List<Vector3>(blades * 4);
            var uv = new List<Vector2>(blades * 4);
            var col = new List<Color>(blades * 4);
            var tri = new List<int>(blades * 6);
            for (int i = 0; i < blades; i++)
            {
                float x = Range(rng, -worldWidth * 0.49f, worldWidth * 0.49f);
                float z = Range(rng, -worldWidth * 0.49f, worldWidth * 0.49f);
                float height = Range(rng, 0.3f, 1.5f);
                float width = Range(rng, 0.06f, 0.16f);
                float angle = Range(rng, 0, Mathf.PI * 2f);
                Vector3 right = new Vector3(Mathf.Cos(angle) * width, 0, Mathf.Sin(angle) * width);
                Vector3 centre = new Vector3(x, GroundHeight(x, z), z);
                float variance = (float)rng.NextDouble();
                int b = v.Count;
                v.Add(centre - right); v.Add(centre + right);
                v.Add(centre - right * 0.35f + Vector3.up * height);
                v.Add(centre + right * 0.35f + Vector3.up * height);
                uv.Add(new Vector2(0, variance)); uv.Add(new Vector2(0, variance));
                uv.Add(new Vector2(1, variance)); uv.Add(new Vector2(1, variance));
                col.Add(new Color(0.55f, 0.66f, 0.67f)); col.Add(new Color(0.55f, 0.66f, 0.67f));
                col.Add(new Color(0.9f, 1f, 0.93f)); col.Add(new Color(0.9f, 1f, 0.93f));
                tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
                tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
            }
            return FromLists("grass", v, tri, uv, col);
        }

        private Mesh BuildStars(int count)
        {
            var rng = new System.Random(seed + 201);
            var v = new List<Vector3>(count * 4);
            var tri = new List<int>(count * 6);
            var uv = new List<Vector2>(count * 4);
            var col = new List<Color>(count * 4);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = new Vector3(Range(rng, -55, 55), Range(rng, 14, 42), Range(rng, 20, 62));
                float r = Range(rng, 0.08f, 0.36f);
                var colour = Color.Lerp(Color.white, new Color(0.43f, 0.76f, 1f), (float)rng.NextDouble());
                int b = v.Count;
                v.Add(p + new Vector3(-r, -r, 0)); v.Add(p + new Vector3(r, -r, 0));
                v.Add(p + new Vector3(-r, r, 0)); v.Add(p + new Vector3(r, r, 0));
                for (int j = 0; j < 4; j++) { uv.Add(Vector2.zero); col.Add(colour); }
                tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
                tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
            }
            return FromLists("star quads", v, tri, uv, col);
        }

        private static void AddCylinder(List<Vector3> v, List<int> tri, List<Vector2> uv,
                                        Vector3 from, Vector3 to, float baseRadius, float endRadius)
        {
            const int sides = 7;
            Vector3 forward = (to - from).normalized;
            Vector3 a = Vector3.Cross(forward, Vector3.up);
            if (a.sqrMagnitude < 0.01f) a = Vector3.Cross(forward, Vector3.forward);
            a.Normalize();
            Vector3 b = Vector3.Cross(forward, a);
            int first = v.Count;
            for (int ring = 0; ring < 2; ring++)
            for (int s = 0; s < sides; s++)
            {
                float angle = s * Mathf.PI * 2f / sides;
                v.Add((ring == 0 ? from : to) + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) *
                    (ring == 0 ? baseRadius : endRadius));
                uv.Add(new Vector2(ring == 0 ? 0 : 1, s / (float)sides));
            }
            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                tri.Add(first + s); tri.Add(first + sides + s); tri.Add(first + next);
                tri.Add(first + next); tri.Add(first + sides + s); tri.Add(first + sides + next);
            }
        }
        private static void AddLeafOctahedron(List<Vector3> v, List<int> tri, List<Vector2> uv,
                                              List<Color> colours, Vector3 centre, float r, float seed)
        {
            int start = v.Count;
            Vector3[] points = {
                centre + Vector3.up * r * 1.35f, centre + Vector3.right * r,
                centre + Vector3.forward * r, centre - Vector3.right * r,
                centre - Vector3.forward * r, centre - Vector3.up * r * 0.9f
            };
            for (int i = 0; i < points.Length; i++)
            {
                v.Add(points[i]);
                uv.Add(new Vector2(i == 0 ? 1 : 0.65f, seed));
                colours.Add(Color.Lerp(new Color(0.65f, 0.9f, 0.75f), new Color(1f, 0.63f, 0.96f), seed));
            }
            int[] faces = { 0,1,2, 0,2,3, 0,3,4, 0,4,1, 5,2,1, 5,3,2, 5,4,3, 5,1,4 };
            foreach (int ix in faces) tri.Add(start + ix);
        }
        private void AddTree(System.Random rng, int index, Vector3 at, float h, float r,
                             Material bark, Material leaves)
        {
            var trunkV = new List<Vector3>(); var trunkT = new List<int>(); var trunkUV = new List<Vector2>();
            var crownV = new List<Vector3>(); var crownT = new List<int>();
            var crownUV = new List<Vector2>(); var crownC = new List<Color>();
            AddCylinder(trunkV, trunkT, trunkUV, Vector3.zero, Vector3.up * h, r * 0.35f, r * 0.07f);
            for (int j = 0; j < 9; j++)
            {
                float angle = j * 2.399963f; // golden angle
                float t = 0.32f + j * 0.069f;
                Vector3 start = Vector3.up * h * t;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 end = start + outward * h * (0.24f + j * 0.012f) + Vector3.up * h * 0.22f;
                AddCylinder(trunkV, trunkT, trunkUV, start, end, r * 0.10f, r * 0.02f);
                float colour = (float)rng.NextDouble();
                AddLeafOctahedron(crownV, crownT, crownUV, crownC, end, r * Range(rng, 1.2f, 2.0f), colour);
            }
            AddLeafOctahedron(crownV, crownT, crownUV, crownC, Vector3.up * h * 1.02f, r * 2f, 0.3f);
            var trunk = FromLists("tree trunk", trunkV, trunkT, trunkUV);
            var canopy = FromLists("canopy", crownV, crownT, crownUV, crownC);
            AddObject("Tree " + index + " trunk", trunk, bark, at);
            AddObject("Tree " + index + " canopy", canopy, leaves, at);
        }

        private Mesh BuildWarpTower()
        {
            const int height = 64;
            const int around = 28;
            var v = new List<Vector3>((height + 1) * (around + 1));
            var tri = new List<int>(height * around * 6);
            var uv = new List<Vector2>(v.Capacity);
            for (int y = 0; y <= height; y++)
            for (int a = 0; a <= around; a++)
            {
                float t = y / (float)height;
                float theta = a / (float)around * 2f * Mathf.PI;
                float ring = 1.7f + Mathf.Sin(t * 25f) * 0.48f;
                float twist = t * 5.5f;
                v.Add(new Vector3(Mathf.Cos(theta + twist) * ring, t * 12f,
                                  Mathf.Sin(theta + twist) * ring));
                uv.Add(new Vector2(t, theta));
            }
            for (int y = 0; y < height; y++)
            for (int a = 0; a < around; a++)
            {
                int n = y * (around + 1) + a;
                tri.Add(n); tri.Add(n + around + 1); tri.Add(n + 1);
                tri.Add(n + 1); tri.Add(n + around + 1); tri.Add(n + around + 2);
            }
            return FromLists("warping-helix", v, tri, uv);
        }
        private void AddObject(string name, Mesh mesh, Material material, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_generated, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            _meshes.Add(mesh);
        }
        private void Clear()
        {
            if (_generated != null) Destroy(_generated.gameObject);
            _generated = null;
            foreach (Mesh mesh in _meshes) if (mesh != null) Destroy(mesh);
            foreach (Material material in _materials) if (material != null) Destroy(material);
            _meshes.Clear(); _materials.Clear();
        }
        private void OnDestroy() => Clear();
    }
}
