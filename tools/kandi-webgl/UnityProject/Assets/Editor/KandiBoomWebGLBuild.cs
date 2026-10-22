using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;
using DanceFlow.Unity.Motion;
using DanceFlow.Unity.Samples;
using DanceFlow.Unity.WebGLDemo;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DanceFlow.Unity.WebGLDemo.Editor
{
    /// <summary>
    /// CI entry point. Fail closed without the SHA-pinned ORIGINAL Kandi Boom FBX and all
    /// four textures. Builds actual Unity WebGL player; no browser-incompatible 3D substitute.
    /// </summary>
    public static class KandiBoomWebGLBuild
    {
        private const string SourceFolder = "Assets/KandiBoomSource";
        private const string Generated = "Assets/KandiBoomGenerated";
        private const string Fbx = SourceFolder + "/kandi-boom.fbx";
        private const string CharacterType = "syn://type/danceflow/motion/rig/custom/kandi-boom";
        private static readonly string[] Names = {
            "Hips", "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
            "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase",
            "Spine02", "Spine01", "Spine", "LeftShoulder", "LeftArm",
            "LeftForeArm", "LeftHand", "RightShoulder", "RightArm", "RightForeArm",
            "RightHand", "neck", "Head", "head_end", "headfront"
        };

        [Serializable] private sealed class AssetItem
        {
            public string name;
            public long bytes;
            public string sha256;
        }
        [Serializable] private sealed class AssetLock
        {
            public string schema;
            public string commit;
            public string target_type;
            public string[] bones;
            public AssetItem[] assets;
        }

        [MenuItem("Tools/DanceFlow/WebGL/Create Verified Kandi Boom Scene")]
        public static void CreateLocalScene()
        {
            VerifyOriginalFiles();
            CreateScene();
        }

        /// <summary>GameCI buildMethod: DanceFlow.Unity.WebGLDemo.Editor.KandiBoomWebGLBuild.Build</summary>
        public static void Build()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                throw new InvalidOperationException("The Unity WebGL demo must be built with BuildTarget.WebGL");
            VerifyOriginalFiles();
            EnsureGeneratedFolder();
            ConfigureUrp();
            ImportAuthenticModel();
            string scene = CreateScene();
            PlayerSettings.productName = "DanceFlow — Kandi Boom";
            PlayerSettings.companyName = "Kieran Simkin";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            QualitySettings.skinWeights = SkinWeights.Unlimited;
            AssetDatabase.SaveAssets();
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string output = Path.Combine(repositoryRoot, "build", "WebGL", "KandiBoom");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var options = new BuildPlayerOptions {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null || report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Unity Kandi Boom WebGL compilation failed: " +
                    (report == null ? "missing build report" : report.summary.result.ToString()));
            string page = Path.Combine(output, "index.html");
            if (!File.Exists(page) || Directory.GetFiles(Path.Combine(output, "Build")).Length == 0)
                throw new IOException("WebGL build has no real Unity player payload/index.html");
            Debug.Log("Verified authentic Kandi Boom Unity WebGL player built at " + output);
        }

        private static string FullPath(string relative)
        {
            if (!relative.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Only Unity Assets-relative paths are supported.", nameof(relative));
            return Path.Combine(Application.dataPath, relative.Substring("Assets/".Length));
        }
        private static string Sha256(string filename)
        {
            using (var stream = File.OpenRead(filename))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static AssetLock VerifyOriginalFiles()
        {
            string lockFile = FullPath(SourceFolder + "/asset-lock.json");
            if (!File.Exists(lockFile)) throw new FileNotFoundException("Pinned original asset-lock.json missing", lockFile);
            var assetLock = JsonUtility.FromJson<AssetLock>(File.ReadAllText(lockFile));
            if (assetLock == null || assetLock.schema != "danceflow.unity.kandi-boom-assets.v1" ||
                assetLock.target_type != CharacterType || assetLock.bones == null ||
                assetLock.bones.Length != Names.Length || assetLock.assets == null || assetLock.assets.Length != 5)
                throw new FormatException("Invalid immutable source asset lock");
            for (int i = 0; i < Names.Length; i++)
                if (assetLock.bones[i] != Names[i]) throw new FormatException("Kandi source skeleton lock changed");
            foreach (var file in assetLock.assets)
            {
                if (file.name != "kandi-boom.fbx" && file.name != "albedo.png" &&
                    file.name != "normal.png" && file.name != "roughness.png" && file.name != "metallic.png")
                    throw new FormatException("Unexpected Kandi file in lock");
                string path = FullPath(SourceFolder + "/" + file.name);
                if (!File.Exists(path) || new FileInfo(path).Length != file.bytes || Sha256(path) != file.sha256)
                    throw new InvalidDataException("Original Kandi Boom source asset not verified: " + file.name);
            }
            return assetLock;
        }

        private static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder(Generated))
                AssetDatabase.CreateFolder("Assets", "KandiBoomGenerated");
        }

        private static void ConfigureUrp()
        {
            EnsureGeneratedFolder();
            string rendererPath = Generated + "/WebGLUniversalRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            string assetPath = Generated + "/WebGLUniversalPipeline.asset";
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                urp = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(urp, assetPath);
            }
            var so = new SerializedObject(urp);
            var data = so.FindProperty("m_RendererDataList");
            if (data == null) throw new InvalidOperationException("Unity URP renderer list changed; do not build without a valid URP asset");
            data.arraySize = 1;
            data.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            var defaultRenderer = so.FindProperty("m_DefaultRendererIndex");
            if (defaultRenderer != null) defaultRenderer.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            if (GraphicsSettings.currentRenderPipeline == null)
                throw new InvalidOperationException("URP setup failed; refusing to build an unlit placeholder");
        }

        private static void ImportAuthenticModel()
        {
            var importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            }
            if (importer == null) throw new InvalidDataException("Unity failed to recognize Kandi Boom's original FBX");
            // Kandi's audited original contains 10 weights on some vertices, not four.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 10;
            importer.minBoneWeight = 0f;
            importer.optimizeGameObjects = false;
            importer.isReadable = true;
            importer.importAnimation = false; // source FBX is a rest-pose-only character
            importer.SaveAndReimport();
            foreach (string file in new[] { "albedo.png", "normal.png", "metallic.png", "roughness.png" })
            {
                string path = SourceFolder + "/" + file;
                var texture = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texture == null) throw new InvalidOperationException("Unity cannot import original PBR map: " + file);
                texture.textureType = file == "normal.png" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (file != "normal.png") texture.sRGBTexture = file == "albedo.png";
                texture.isReadable = file == "metallic.png" || file == "roughness.png";
                texture.wrapMode = TextureWrapMode.Repeat;
                texture.SaveAndReimport();
            }
        }

        private static Texture2D OriginalTexture(string file)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceFolder + "/" + file);
            if (tex == null) throw new InvalidOperationException("Missing original Kandi texture: " + file);
            return tex;
        }
        private static Texture2D MetallicSmoothness()
        {
            string name = Generated + "/MetallicSmoothness.png";
            if (!File.Exists(FullPath(name)))
            {
                Texture2D metal = OriginalTexture("metallic.png");
                Texture2D rough = OriginalTexture("roughness.png");
                if (metal.width != rough.width || metal.height != rough.height)
                    throw new InvalidDataException("Metallic and roughness maps must have matching dimensions");
                Color32[] metalPixel = metal.GetPixels32();
                Color32[] roughPixel = rough.GetPixels32();
                var output = new Color32[metalPixel.Length];
                for (int i = 0; i < output.Length; i++)
                {
                    // URP Lit consumes metallic from R, smoothness from A. Roughness must invert.
                    byte metallic = metalPixel[i].r;
                    byte smoothness = (byte)(255 - roughPixel[i].r);
                    output[i] = new Color32(metallic, 0, 0, smoothness);
                }
                var texture = new Texture2D(metal.width, metal.height, TextureFormat.RGBA32, false, true);
                texture.SetPixels32(output);
                texture.Apply();
                File.WriteAllBytes(FullPath(name), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(name, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(name) as TextureImporter;
            if (importer == null) throw new IOException("Packed metallic/smoothness texture did not import");
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(name);
        }
        private static Material KandiMaterial(Material originalImportedMaterial)
        {
            string path = Generated + "/KandiBoomURP.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Universal Render Pipeline Lit shader is not supported");
            material = new Material(shader);
            material.name = "Kandi Boom — original albedo and normal/PBR, preserved UVs";
            material.SetTexture("_BaseMap", OriginalTexture("albedo.png"));
            // Retain FBX importer's original UV/sampler transform instead of inventing a V flip.
            Vector2 uvScale = originalImportedMaterial != null ? originalImportedMaterial.mainTextureScale : Vector2.one;
            Vector2 uvOffset = originalImportedMaterial != null ? originalImportedMaterial.mainTextureOffset : Vector2.zero;
            material.SetTextureScale("_BaseMap", uvScale);
            material.SetTextureOffset("_BaseMap", uvOffset);
            material.SetTexture("_BumpMap", OriginalTexture("normal.png"));
            material.SetTextureScale("_BumpMap", uvScale);
            material.SetTextureOffset("_BumpMap", uvOffset);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", MetallicSmoothness());
            material.SetTextureScale("_MetallicGlossMap", uvScale);
            material.SetTextureOffset("_MetallicGlossMap", uvOffset);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static string CreateScene()
        {
            EnsureGeneratedFolder();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (asset == null) throw new InvalidOperationException("Original Kandi Boom FBX prefab is missing after Unity import");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (character == null) throw new InvalidOperationException("Failed to instantiate original Kandi Boom FBX");
            character.name = "Kandi Boom — original verified DanceRudiments FBX";
            var skins = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length != 1 || skins[0].sharedMesh == null ||
                skins[0].bones.Length < 22 || skins[0].bones.Length > 24)
                throw new InvalidDataException("Kandi FBX must import one skinned mesh with its audited influencing bones");
            // Source audit: 24 skeleton nodes, of which 22 influence skin; two head helpers
            // have zero influence and need not appear in SkinnedMeshRenderer.bones.
            var bones = new HashSet<string>(character.GetComponentsInChildren<Transform>(true)
                .Select(t => t.name), StringComparer.Ordinal);
            if (!Names.All(bones.Contains) || skins[0].sharedMesh.vertexCount < 10000 ||
                skins[0].sharedMesh.triangles.Length / 3 < 29000 ||
                skins[0].sharedMesh.uv.Length == 0)
                throw new InvalidDataException("Imported Kandi mesh/UV/bones differ from the source rig audit");
            skins[0].quality = SkinQuality.Auto;
            var material = KandiMaterial(skins[0].sharedMaterial);
            skins[0].sharedMaterials = Enumerable.Repeat(material, skins[0].sharedMaterials.Length).ToArray();
            character.transform.position = new Vector3(-5, 0, 0);
            float height = skins[0].bounds.size.y;
            if (height < 0.0001f) throw new InvalidDataException("Kandi skinned mesh has invalid bounds");
            character.transform.localScale *= 2.4f / height;
            character.transform.position += Vector3.up * (0.4f - skins[0].bounds.min.y);

            var systems = new GameObject("DanceFlow — single authoritative clock");
            var songClock = systems.AddComponent<DanceFlowClockBehaviour>();
            var clockShader = systems.AddComponent<DanceFlowShaderBridge>();
            clockShader.SetClock(songClock);
            var signals = systems.AddComponent<MotionSignalBus>();
            signals.Configure(songClock);
            systems.AddComponent<ProceduralLandscape>();
            systems.AddComponent<KandiBoomControls>().Configure(songClock, 120);
            systems.AddComponent<KandiBoomBeatDance>().Configure(songClock, character.transform);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 250f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.09f);
            camera.gameObject.AddComponent<KandiCameraOrbit>().Configure(new Vector3(-4.7f, 2.1f, 0), 14);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(48, -33, 0);
            light.color = new Color(0.81f, 0.87f, 1f);
            light.intensity = 1.35f;
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.transform.position = new Vector3(-7, 4.5f, -4);
            fill.range = 12;
            fill.intensity = 6;
            fill.color = new Color(0.84f, 0.39f, 0.84f);
            var landscape = systems.GetComponent<ProceduralLandscape>();
            Shader landscapeShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/DanceFlowLandscape/MotionLandscape.shader");
            if (landscapeShader == null) throw new IOException("Beat-reactive landscape shader not imported");
            var so = new SerializedObject(landscape);
            so.FindProperty("landscapeShader").objectReferenceValue = landscapeShader;
            so.FindProperty("grassBlades").intValue = 2500;
            so.FindProperty("treeCount").intValue = 12;
            so.FindProperty("includeWarpSculpture").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            string scenePath = Generated + "/OriginalKandiBoomWebGL.unity";
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException("Unity refused to save original Kandi Boom demonstration scene");
            return scenePath;
        }
    }
}
