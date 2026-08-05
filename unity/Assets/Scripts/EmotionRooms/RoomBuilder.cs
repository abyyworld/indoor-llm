// Procedural greybox for both room shells, built to scene brief section 2 exactly.
//
// Why generated rather than modelled by hand: the brief matches the two conditions on
// entrance width, depth, ceiling height, standing position and both sightlines, and
// deliberately lets only floor area differ. Those constraints are arithmetic, so building
// them in code means they are checked rather than eyeballed, and a dimension change is a
// number here rather than a remodelling job.
//
// Unity menu: Emotion Rooms > Build Both Shells.
//
// What this does NOT do, on purpose: it never touches wall colour, roughness or light
// intensity. Those come from RoomConfig via RoomLoader at load time. This only builds
// geometry, and the loader must never move it (CLAUDE.md invariant 6).

using System.Collections.Generic;
using UnityEngine;

namespace EmotionRooms
{
    public static class RoomDimensions
    {
        // Scene brief section 2. Shared by both conditions.
        public const float EntranceWidth = 4.2f;   // = vault diameter in the curved shell
        public const float Depth = 4.3f;
        public const float CeilingHeight = 2.4f;   // UK residential practice, settled
        public const float StandingFromEntrance = 1.3f;

        // Curved condition splits its depth into a straight foyer plus a semicircular vault.
        public const float FoyerDepth = 2.2f;
        public const float VaultRadius = 2.1f;     // FoyerDepth + VaultRadius == Depth

        // The matched sightlines. These are assertions, not inputs: if the geometry stops
        // satisfying them the build is wrong.
        public const float ToSideWall = 2.1f;
        public const float ToFacingWall = 3.0f;

        public const float WallThickness = 0.05f;

        /// <summary>Standing position, on the floor, centred on width.</summary>
        public static Vector3 StandingPosition
        {
            get { return new Vector3(0f, 0f, StandingFromEntrance); }
        }

        /// <summary>
        /// Check the brief's own arithmetic. Called by the builder so a bad edit fails
        /// loudly instead of producing a quietly wrong room.
        /// </summary>
        public static List<string> Validate()
        {
            var errors = new List<string>();

            if (!Mathf.Approximately(FoyerDepth + VaultRadius, Depth))
                errors.Add(string.Format(
                    "curved depth {0} + {1} != linear depth {2}", FoyerDepth, VaultRadius, Depth));

            if (!Mathf.Approximately(VaultRadius * 2f, EntranceWidth))
                errors.Add(string.Format(
                    "vault diameter {0} != entrance width {1}", VaultRadius * 2f, EntranceWidth));

            if (!Mathf.Approximately(EntranceWidth / 2f, ToSideWall))
                errors.Add(string.Format(
                    "half-width {0} != stated side sightline {1}", EntranceWidth / 2f, ToSideWall));

            if (!Mathf.Approximately(Depth - StandingFromEntrance, ToFacingWall))
                errors.Add(string.Format(
                    "depth - standing {0} != stated facing sightline {1}",
                    Depth - StandingFromEntrance, ToFacingWall));

            return errors;
        }

        /// <summary>Linear floor area. The brief states about 18.1 m^2.</summary>
        public static float LinearArea { get { return EntranceWidth * Depth; } }

        /// <summary>
        /// Curved floor area: the foyer rectangle plus a half disc. The brief states
        /// about 16.2 m^2, and the ~2 m^2 shortfall against the linear shell is the one
        /// intended difference between the conditions. Do not try to remove it.
        /// </summary>
        public static float CurvedArea
        {
            get
            {
                return EntranceWidth * FoyerDepth + (Mathf.PI * VaultRadius * VaultRadius) / 2f;
            }
        }
    }

    public static class RoomBuilder
    {
        const int VaultSegments = 48;   // smooth enough at 2.1 m without wasting verts

        /// <summary>Builds both shells under one parent, ready for RoomLoader.</summary>
        public static GameObject BuildAll(Transform parent = null)
        {
            var errors = RoomDimensions.Validate();
            if (errors.Count > 0)
                throw new System.InvalidOperationException(
                    "RoomDimensions is internally inconsistent:\n  - " +
                    string.Join("\n  - ", errors.ToArray()));

            var root = new GameObject("EmotionRooms");
            if (parent != null) root.transform.SetParent(parent, false);

            var linear = BuildLinear();
            linear.transform.SetParent(root.transform, false);

            var curved = BuildCurved();
            curved.transform.SetParent(root.transform, false);

            // Between-subjects: exactly one is ever active. The loader toggles them, and
            // the inactive one must be genuinely inactive so it cannot leak light.
            linear.SetActive(true);
            curved.SetActive(false);

            var marker = new GameObject("Standing Position");
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = RoomDimensions.StandingPosition;

            return root;
        }

        public static GameObject BuildLinear()
        {
            var root = new GameObject("Linear Room Root");

            float w = RoomDimensions.EntranceWidth;
            float d = RoomDimensions.Depth;
            float h = RoomDimensions.CeilingHeight;

            AddSurface(root, "Floor", new Vector3(0, 0, d / 2f), new Vector3(w, RoomDimensions.WallThickness, d));
            AddSurface(root, "Ceiling", new Vector3(0, h, d / 2f), new Vector3(w, RoomDimensions.WallThickness, d));
            AddSurface(root, "Wall Entrance", new Vector3(0, h / 2f, 0), new Vector3(w, h, RoomDimensions.WallThickness));
            AddSurface(root, "Wall Facing", new Vector3(0, h / 2f, d), new Vector3(w, h, RoomDimensions.WallThickness));
            AddSurface(root, "Wall Left", new Vector3(-w / 2f, h / 2f, d / 2f), new Vector3(RoomDimensions.WallThickness, h, d));
            AddSurface(root, "Wall Right", new Vector3(w / 2f, h / 2f, d / 2f), new Vector3(RoomDimensions.WallThickness, h, d));

            AddFurniture(root, d);
            return root;
        }

        public static GameObject BuildCurved()
        {
            var root = new GameObject("Curved Room Root");

            float w = RoomDimensions.EntranceWidth;
            float h = RoomDimensions.CeilingHeight;
            float foyer = RoomDimensions.FoyerDepth;
            float r = RoomDimensions.VaultRadius;

            // Straight-walled foyer. The brief keeps this rather than curving throughout,
            // because a participant can turn to face any direction and a half-open shape
            // has no standable real-world equivalent.
            AddSurface(root, "Floor Foyer", new Vector3(0, 0, foyer / 2f), new Vector3(w, RoomDimensions.WallThickness, foyer));
            AddSurface(root, "Ceiling Foyer", new Vector3(0, h, foyer / 2f), new Vector3(w, RoomDimensions.WallThickness, foyer));
            AddSurface(root, "Wall Entrance", new Vector3(0, h / 2f, 0), new Vector3(w, h, RoomDimensions.WallThickness));
            AddSurface(root, "Wall Left Foyer", new Vector3(-w / 2f, h / 2f, foyer / 2f), new Vector3(RoomDimensions.WallThickness, h, foyer));
            AddSurface(root, "Wall Right Foyer", new Vector3(w / 2f, h / 2f, foyer / 2f), new Vector3(RoomDimensions.WallThickness, h, foyer));

            // Semicircular vault beyond the springline, which is where the foyer ends.
            var vault = BuildVault(new Vector3(0, 0, foyer), r, h);
            vault.transform.SetParent(root.transform, false);

            AddFurniture(root, RoomDimensions.Depth);
            return root;
        }

        /// <summary>
        /// The vault: a half-cylinder wall plus its floor and ceiling caps, centred on the
        /// springline. Generated rather than primitive-built because Unity has no half-disc.
        /// </summary>
        static GameObject BuildVault(Vector3 springlineCentre, float radius, float height)
        {
            var go = new GameObject("Vault");
            go.transform.localPosition = springlineCentre;

            var wallVerts = new List<Vector3>();
            var wallTris = new List<int>();
            var wallUvs = new List<Vector2>();

            var floorVerts = new List<Vector3> { Vector3.zero };
            var floorTris = new List<int>();
            var ceilVerts = new List<Vector3> { new Vector3(0, height, 0) };
            var ceilTris = new List<int>();

            // Sweep 0..pi so the arc runs from the right springline round to the left.
            for (int i = 0; i <= VaultSegments; i++)
            {
                float t = (float)i / VaultSegments;
                float angle = t * Mathf.PI;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                wallVerts.Add(new Vector3(x, 0f, z));
                wallVerts.Add(new Vector3(x, height, z));
                wallUvs.Add(new Vector2(t * Mathf.PI * radius, 0f));
                wallUvs.Add(new Vector2(t * Mathf.PI * radius, height));

                floorVerts.Add(new Vector3(x, 0f, z));
                ceilVerts.Add(new Vector3(x, height, z));

                if (i < VaultSegments)
                {
                    int b = i * 2;
                    // Wound so the faces point inward, into the room.
                    wallTris.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
                    floorTris.AddRange(new[] { 0, i + 2, i + 1 });
                    ceilTris.AddRange(new[] { 0, i + 1, i + 2 });
                }
            }

            AddMesh(go, "Vault Wall", wallVerts, wallTris, wallUvs);
            AddMesh(go, "Vault Floor", floorVerts, floorTris, null);
            AddMesh(go, "Vault Ceiling", ceilVerts, ceilTris, null);
            return go;
        }

        static void AddMesh(GameObject parent, string name, List<Vector3> verts, List<int> tris, List<Vector2> uvs)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = DefaultSurface();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            // Tagged so RoomLoader can collect the tintable surfaces without hand-wiring.
            go.AddComponent<TintableSurface>();
        }

        static void AddSurface(GameObject parent, string name, Vector3 centre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = DefaultSurface();
            go.AddComponent<TintableSurface>();
        }

        /// <summary>
        /// Fixed naturalistic furnishing. Final list confirmed by Mengkai, 2 Aug 2026:
        /// sofa, armchair, coffee table with a teacup, rug, bookshelf, two wall art
        /// pieces. Identical in both shells and in every emotion condition, never
        /// manipulated.
        ///
        /// Primitives on purpose: the brief asks that these stay easily swappable
        /// placeholders rather than having this geometry hard-coded into scene logic.
        /// None carry TintableSurface, so the manipulation can never leak onto furniture.
        ///
        /// Placement note: the bookshelf goes on a side wall rather than the far wall so
        /// it does not block the sightline the two shapes are matched on, and the
        /// armchair is offset rather than centred so the sofa keeps the symmetric
        /// position the brief specifies.
        /// </summary>
        static void AddFurniture(GameObject root, float depth)
        {
            var furniture = new GameObject("Fixed Furnishing");
            furniture.transform.SetParent(root.transform, false);

            float halfWidth = RoomDimensions.EntranceWidth / 2f;

            // Three-seat sofa against the far wall, centred on width.
            AddProp(furniture, "Sofa (placeholder)",
                new Vector3(0f, 0.4f, depth - 0.45f), new Vector3(2.1f, 0.8f, 0.85f));

            // Armchair, offset to one side, angled in toward the coffee table.
            AddProp(furniture, "Armchair (placeholder)",
                new Vector3(-1.35f, 0.4f, depth - 1.45f), new Vector3(0.8f, 0.8f, 0.8f),
                yaw: 55f);

            // Coffee table in front of the sofa.
            AddProp(furniture, "Coffee Table (placeholder)",
                new Vector3(0f, 0.2f, depth - 1.5f), new Vector3(1.1f, 0.4f, 0.6f));

            // Teacup on the table. Small, but it is in the list and it is the kind of
            // detail that makes a greybox read as a room rather than a diagram.
            AddProp(furniture, "Teacup (placeholder)",
                new Vector3(0.18f, 0.44f, depth - 1.5f), new Vector3(0.08f, 0.08f, 0.08f));

            // Rug under the table and the sofa's front portion.
            AddProp(furniture, "Rug (placeholder)",
                new Vector3(0f, 0.01f, depth - 1.2f), new Vector3(2.4f, 0.02f, 1.6f));

            // Bookshelf against a side wall, clear of the matched facing sightline.
            AddProp(furniture, "Bookshelf (placeholder)",
                new Vector3(halfWidth - 0.2f, 0.9f, depth - 2.2f),
                new Vector3(0.35f, 1.8f, 0.9f));

            // Two wall art pieces above and behind the sofa, symmetric about centre.
            AddProp(furniture, "Wall Art A (placeholder)",
                new Vector3(-0.55f, 1.6f, depth - 0.12f), new Vector3(0.7f, 0.5f, 0.04f));
            AddProp(furniture, "Wall Art B (placeholder)",
                new Vector3(0.55f, 1.6f, depth - 0.12f), new Vector3(0.7f, 0.5f, 0.04f));
        }

        static void AddProp(GameObject parent, string name, Vector3 centre, Vector3 size,
                            float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = DefaultSurface();
        }

        static Material cachedSurface;

        static Material DefaultSurface()
        {
            if (cachedSurface != null) return cachedSurface;

            // URP first, per build-decisions.md section 2, falling back so this still
            // builds in a project that has not had URP installed yet.
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            cachedSurface = new Material(shader) { name = "Greybox Surface" };
            return cachedSurface;
        }
    }

    /// <summary>
    /// Marks a surface whose colour and roughness the config drives. Walls, floors and
    /// ceilings carry this; furniture deliberately does not.
    /// </summary>
    public class TintableSurface : MonoBehaviour { }
}
