// Reads a room config and builds the room (design-spec.md sections 1.4 and 8.3).
//
// Scope is exactly what the spec allows the config to touch:
//   hue + saturation -> wall albedo
//   texture          -> wall greyscale map, tiling and smoothness
//   brightness       -> intensity of a neutral-white light
//   shape            -> which pre-built room root is active (researcher-set factor)
//
// Everything else in the scene -- dimensions, furniture, object positions, the spawn
// point -- is authored by hand and this component must never move it.
//
// Setup and manual test procedure: see unity/README.md.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EmotionRooms
{
    /// <summary>One entry in the texture lookup table, keyed by the pool value.</summary>
    [Serializable]
    public class WallTexture
    {
        [Tooltip("Must match a value in PoolConstants.Textures exactly.")]
        public string name;

        [Tooltip("Greyscale (black and white) map, so the wall hue tints it.")]
        public Texture2D greyscaleMap;

        [Range(0f, 1f)]
        public float smoothness = 0.5f;

        [Tooltip("UV tiling for this material. A fixed property of the texture, not a variable.")]
        public float tiling = 1f;
    }

    [DisallowMultipleComponent]
    public class RoomLoader : MonoBehaviour
    {
        [Header("Config source")]
        [Tooltip("A single room config, or a batch/session file with a 'rooms' array.")]
        public TextAsset configAsset;

        [Tooltip("Treat configAsset as a batch and load the first room on start.")]
        public bool configAssetIsBatch = false;

        public bool loadOnStart = true;

        [Header("Scene wiring")]
        [Tooltip("Every renderer whose material carries the wall colour and texture.")]
        public Renderer[] wallRenderers;

        [Tooltip("The room's single light source. Stays neutral white; only intensity varies.")]
        public Light roomLight;

        [Header("Wall textures (greyscale)")]
        public WallTexture[] wallTextures;

        [Header("Brightness mapping")]
        [Tooltip("Light intensity when brightness is at the bottom of the pool.")]
        public float minIntensity = 0.15f;

        [Tooltip("Light intensity when brightness is at the top of the pool.")]
        public float maxIntensity = 2.5f;

        [Tooltip("Also scale ambient light. Without this a 'dim' room still reads as flat-lit.")]
        public bool scaleAmbient = true;
        public float minAmbient = 0.05f;
        public float maxAmbient = 1.0f;

        [Header("Room shape (researcher-set factor)")]
        [Tooltip("Leave both empty if shape is not part of your design.")]
        public GameObject linearRoomRoot;
        public GameObject curvedRoomRoot;

        [Header("Debug")]
        public bool logOnLoad = true;

        /// <summary>The config currently built into the scene, or null.</summary>
        public RoomConfig Current { get; private set; }

        /// <summary>Fires after the room is fully applied. Hook the trial timer here.</summary>
        public event Action<RoomConfig> RoomLoaded;

        RoomBatch loadedBatch;
        int batchIndex = -1;
        Material wallMaterial;

        // Property names differ between the built-in and URP/HDRP shaders, so resolve
        // whichever the assigned material actually has.
        static readonly string[] ColorProperties = { "_BaseColor", "_Color" };
        static readonly string[] MapProperties = { "_BaseMap", "_MainTex" };
        static readonly string[] SmoothnessProperties = { "_Smoothness", "_Glossiness" };

        void Start()
        {
            if (!loadOnStart)
            {
                return;
            }

            if (configAsset == null)
            {
                Debug.LogError("[RoomLoader] No configAsset assigned; nothing to load.", this);
                return;
            }

            if (configAssetIsBatch)
            {
                LoadBatchFromJson(configAsset.text);
                LoadBatchIndex(0);
            }
            else
            {
                LoadFromJson(configAsset.text);
            }
        }

        // ------------------------------------------------------------------ loading

        /// <summary>Parse, validate and build a single room config.</summary>
        /// <summary>
        /// Deactivate both shells, leaving the participant looking at nothing.
        ///
        /// Used between trials: the room must be out of sight before the affect grid
        /// appears, because someone rating a room they can still see is describing what
        /// is in front of them rather than how it made them feel.
        /// </summary>
        [Tooltip("Neutral ground raised whenever the rooms come down.\n\n" +
                 "Owned here rather than by each caller because every phase hides the " +
                 "room before it asks anything -- Phase A before the grid, Phase B " +
                 "before all three questions -- and each one of them was leaving the " +
                 "participant floating in an empty skybox.")]
        public RatingStage ratingStage;

        public void HideRooms()
        {
            if (linearRoomRoot != null) linearRoomRoot.SetActive(false);
            if (curvedRoomRoot != null) curvedRoomRoot.SetActive(false);
            EnsureRatingStage();
            if (ratingStage != null) ratingStage.Show();
        }

        /// <summary>
        /// Built on first use rather than saved in the scene: the player deserializes the
        /// scene positionally, so every component in that file is another chance for a
        /// build to die before it starts.
        /// </summary>
        void EnsureRatingStage()
        {
            if (ratingStage != null) return;

            var host = new GameObject("Rating Stage");
            host.transform.position = new Vector3(0f, RoomDimensions.StandingPosition.y, 0f);
            ratingStage = host.AddComponent<RatingStage>();
        }

        public void LoadFromJson(string json)
        {
            Load(RoomConfig.FromJson(json));
        }

        /// <summary>Load a config written next to the build at runtime (headset workflow).</summary>
        public void LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new RoomConfigException("No config file at: " + path);
            }
            LoadFromJson(File.ReadAllText(path));
        }

        /// <summary>Parse and validate a whole batch up front, without building anything.</summary>
        public RoomBatch LoadBatchFromJson(string json)
        {
            loadedBatch = RoomBatch.FromJson(json);
            batchIndex = -1;
            if (logOnLoad)
            {
                Debug.Log("[RoomLoader] Batch validated: " + loadedBatch.rooms.Length + " rooms.", this);
            }
            return loadedBatch;
        }

        public void LoadBatchFromFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new RoomConfigException("No batch file at: " + path);
            }
            LoadBatchFromJson(File.ReadAllText(path));
        }

        public void LoadRoomById(string roomId)
        {
            RequireBatch();
            Load(loadedBatch.Find(roomId));
        }

        public void LoadBatchIndex(int index)
        {
            RequireBatch();
            if (index < 0 || index >= loadedBatch.rooms.Length)
            {
                throw new RoomConfigException(
                    "Batch index " + index + " is out of range (0.." + (loadedBatch.rooms.Length - 1) + ").");
            }
            batchIndex = index;
            Load(loadedBatch.rooms[index]);
        }

        /// <summary>Build a room. The config is re-validated even if it came from code.</summary>
        public void Load(RoomConfig config)
        {
            if (config == null)
            {
                throw new RoomConfigException("Cannot load a null config.");
            }
            config.AssertValid();

            // The stage comes down as the room goes up, so there is never a frame with
            // both and never a frame with neither.
            if (ratingStage != null) ratingStage.Hide();

            ApplyShape(config);
            ApplyWalls(config);
            ApplyLight(config);

            Current = config;

            if (logOnLoad)
            {
                Debug.Log("[RoomLoader] Loaded " + config, this);
            }

            if (RoomLoaded != null)
            {
                RoomLoaded(config);
            }
        }

        // ------------------------------------------------------------------ applying

        void ApplyShape(RoomConfig config)
        {
            if (linearRoomRoot == null && curvedRoomRoot == null)
            {
                return;
            }
            if (!config.HasShape)
            {
                // Shape roots exist but the config does not say which: leave the scene
                // as the researcher set it rather than guessing a condition.
                return;
            }

            bool linear = config.shape == "linear";
            if (linearRoomRoot != null)
            {
                linearRoomRoot.SetActive(linear);
            }
            if (curvedRoomRoot != null)
            {
                curvedRoomRoot.SetActive(!linear);
            }
        }

        void ApplyWalls(RoomConfig config)
        {
            if (wallRenderers == null || wallRenderers.Length == 0)
            {
                throw new RoomConfigException("[RoomLoader] wallRenderers is empty; assign the wall renderers.");
            }

            WallTexture wallTexture = FindTexture(config.texture);
            Material material = EnsureWallMaterial();

            SetColor(material, config.WallColor());
            SetMap(material, wallTexture.greyscaleMap, wallTexture.tiling);
            SetSmoothness(material, SmoothnessFor(config, wallTexture));

            for (int i = 0; i < wallRenderers.Length; i++)
            {
                if (wallRenderers[i] == null)
                {
                    Debug.LogWarning("[RoomLoader] wallRenderers[" + i + "] is empty; skipping.", this);
                    continue;
                }
                wallRenderers[i].sharedMaterial = material;
            }
        }

        void ApplyLight(RoomConfig config)
        {
            if (roomLight == null)
            {
                throw new RoomConfigException("[RoomLoader] roomLight is not assigned.");
            }

            // Neutral white: hue lives on the walls only.
            roomLight.color = Color.white;
            roomLight.intensity = config.LightIntensity(minIntensity, maxIntensity);

            if (scaleAmbient)
            {
                RenderSettings.ambientIntensity = Mathf.Lerp(minAmbient, maxAmbient, config.brightness);
            }
        }

        [Header("Roughness")]
        [Tooltip("Material smoothness when the config says 'smooth'.")]
        [Range(0f, 1f)] public float smoothWhenSmooth = 0.55f;

        [Tooltip("Material smoothness when the config says 'rough'.")]
        [Range(0f, 1f)] public float smoothWhenRough = 0.05f;

        /// <summary>
        /// Smoothness for this room. Driven by the config's roughness, not by the texture.
        ///
        /// `roughness` is one of the five variables the model controls, and nothing was
        /// reading it: smoothness came from the per-texture constant alone, so rough and
        /// smooth rendered identically and the variable was manipulated in the data while
        /// being invisible in the headset. Texture supplies the pattern, roughness the
        /// specular response, so the two read as separate properties rather than one.
        ///
        /// The texture's own value is kept as a small offset, because plaster and textile
        /// are not equally shiny even at the same roughness setting.
        /// </summary>
        float SmoothnessFor(RoomConfig config, WallTexture wallTexture)
        {
            bool smooth = config.Roughness == "smooth";
            float basis = smooth ? smoothWhenSmooth : smoothWhenRough;

            // Texture nudges it by up to +-0.1 around the roughness level, never enough
            // to make a rough wall read as smoother than a smooth one.
            float offset = (wallTexture.smoothness - 0.2f) * 0.5f;
            return Mathf.Clamp01(basis + Mathf.Clamp(offset, -0.1f, 0.1f));
        }

        WallTexture FindTexture(string textureName)
        {
            if (wallTextures != null)
            {
                for (int i = 0; i < wallTextures.Length; i++)
                {
                    if (wallTextures[i] != null && wallTextures[i].name == textureName)
                    {
                        if (wallTextures[i].greyscaleMap == null)
                        {
                            throw new RoomConfigException(
                                "Texture '" + textureName + "' has no greyscaleMap assigned.");
                        }
                        return wallTextures[i];
                    }
                }
            }

            throw new RoomConfigException(
                "No wall texture registered for '" + textureName + "'. The pool is: " +
                PoolConstants.Join(PoolConstants.Textures) +
                " -- every pool value needs an entry in wallTextures.");
        }

        Material EnsureWallMaterial()
        {
            if (wallMaterial != null)
            {
                return wallMaterial;
            }

            Renderer source = null;
            for (int i = 0; i < wallRenderers.Length && source == null; i++)
            {
                if (wallRenderers[i] != null)
                {
                    source = wallRenderers[i];
                }
            }
            if (source == null)
            {
                throw new RoomConfigException(
                    "[RoomLoader] wallRenderers holds no live renderer. Rebuild the scene " +
                    "from the Study Control Panel.");
            }

            if (source.sharedMaterial == null)
            {
                // Recoverable, so recover. A null material here used to throw and end the
                // session on trial one; it means the surfaces were built with a material
                // that was never saved as an asset, not that anything is mis-wired. The
                // colour and roughness are about to be overwritten from the config
                // anyway, so a fresh standard material loses nothing.
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                wallMaterial = new Material(shader) { name = "Room Surface (recovered)" };
                Debug.LogWarning("[RoomLoader] Wall renderers had no material, so one was " +
                                 "created. Rebuild the scene from the Study Control Panel " +
                                 "to save materials as assets and stop this recurring.");
                return wallMaterial;
            }

            // One runtime instance shared by every wall, so we never write into the
            // project asset -- editing that would leak between rooms and get committed.
            wallMaterial = new Material(source.sharedMaterial);
            wallMaterial.name = source.sharedMaterial.name + " (room instance)";
            return wallMaterial;
        }

        void OnDestroy()
        {
            if (wallMaterial == null)
            {
                return;
            }

            // The context-menu items can create the instance in edit mode, where Destroy
            // is not allowed and logs an error instead of freeing anything.
            if (Application.isPlaying)
            {
                Destroy(wallMaterial);
            }
            else
            {
                DestroyImmediate(wallMaterial);
            }
            wallMaterial = null;
        }

        void RequireBatch()
        {
            if (loadedBatch == null)
            {
                throw new RoomConfigException("No batch loaded; call LoadBatchFromJson or LoadBatchFromFile first.");
            }
        }

        static void SetColor(Material material, Color color)
        {
            string property = FirstProperty(material, ColorProperties);
            if (property == null)
            {
                throw new RoomConfigException(
                    "Wall material '" + material.name + "' has no colour property (_BaseColor or _Color).");
            }
            material.SetColor(property, color);
        }

        static void SetMap(Material material, Texture2D map, float tiling)
        {
            string property = FirstProperty(material, MapProperties);
            if (property == null)
            {
                throw new RoomConfigException(
                    "Wall material '" + material.name + "' has no albedo map property (_BaseMap or _MainTex).");
            }
            material.SetTexture(property, map);
            material.SetTextureScale(property, new Vector2(tiling, tiling));
        }

        static void SetSmoothness(Material material, float smoothness)
        {
            string property = FirstProperty(material, SmoothnessProperties);
            if (property != null)
            {
                material.SetFloat(property, smoothness);
            }
            // Not every shader exposes smoothness; texture feel is secondary, so a
            // material without it is not worth failing a trial over.
        }

        static string FirstProperty(Material material, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (material.HasProperty(candidates[i]))
                {
                    return candidates[i];
                }
            }
            return null;
        }

        // ------------------------------------------------------ editor conveniences

        [ContextMenu("Load Config Asset")]
        void ContextLoadConfigAsset()
        {
            if (configAsset == null)
            {
                Debug.LogError("[RoomLoader] No configAsset assigned.", this);
                return;
            }
            if (configAssetIsBatch)
            {
                LoadBatchFromJson(configAsset.text);
                LoadBatchIndex(0);
            }
            else
            {
                LoadFromJson(configAsset.text);
            }
        }

        [ContextMenu("Load Next Room In Batch")]
        void ContextLoadNext()
        {
            if (loadedBatch == null && configAsset != null)
            {
                LoadBatchFromJson(configAsset.text);
            }
            RequireBatch();
            LoadBatchIndex((batchIndex + 1) % loadedBatch.rooms.Length);
        }

        [ContextMenu("Validate Config Asset Only")]
        void ContextValidateOnly()
        {
            if (configAsset == null)
            {
                Debug.LogError("[RoomLoader] No configAsset assigned.", this);
                return;
            }
            try
            {
                if (configAssetIsBatch)
                {
                    RoomBatch batch = RoomBatch.FromJson(configAsset.text);
                    Debug.Log("[RoomLoader] Valid batch: " + batch.rooms.Length + " rooms.", this);
                }
                else
                {
                    Debug.Log("[RoomLoader] Valid config: " + RoomConfig.FromJson(configAsset.text), this);
                }
            }
            catch (RoomConfigException exception)
            {
                Debug.LogError("[RoomLoader] " + exception.Message, this);
            }
        }

        /// <summary>Warn in the inspector about wiring that only fails at trial time.</summary>
        void OnValidate()
        {
            if (minIntensity > maxIntensity)
            {
                Debug.LogWarning("[RoomLoader] minIntensity is above maxIntensity.", this);
            }

            if (wallTextures == null)
            {
                return;
            }

            List<string> missing = new List<string>();
            for (int i = 0; i < PoolConstants.Textures.Length; i++)
            {
                string poolValue = PoolConstants.Textures[i];
                bool found = false;
                for (int j = 0; j < wallTextures.Length && !found; j++)
                {
                    found = wallTextures[j] != null && wallTextures[j].name == poolValue;
                }
                if (!found)
                {
                    missing.Add(poolValue);
                }
            }
            if (missing.Count > 0 && wallTextures.Length > 0)
            {
                Debug.LogWarning(
                    "[RoomLoader] No wallTextures entry for: " + string.Join(", ", missing.ToArray()) +
                    ". Configs choosing those will fail to load.", this);
            }
        }
    }
}
