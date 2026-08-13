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
            var floorUvs = new List<Vector2> { Vector2.zero };
            var ceilVerts = new List<Vector3> { new Vector3(0, height, 0) };
            var ceilTris = new List<int>();
            var ceilUvs = new List<Vector2> { Vector2.zero };

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
                // Planar map in metres, same scale as every wall, so the caps carry the
                // same grain instead of a single smeared texel that read as an opening.
                floorUvs.Add(new Vector2(x, z));
                ceilUvs.Add(new Vector2(x, z));

                if (i < VaultSegments)
                {
                    int b = i * 2;
                    // Wound so the faces point INWARD. The previous winding read as
                    // inward in a comment and was outward in fact: Unity front faces are
                    // the clockwise side, and from inside the room this order came out
                    // counterclockwise -- so the whole curved wall was backface-culled
                    // and half the room showed the skybox.
                    wallTris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                    floorTris.AddRange(new[] { 0, i + 2, i + 1 });
                    ceilTris.AddRange(new[] { 0, i + 1, i + 2 });
                }
            }

            AddMesh(go, "Vault Wall", wallVerts, wallTris, wallUvs);
            AddMesh(go, "Vault Floor", floorVerts, floorTris, floorUvs);
            AddMesh(go, "Vault Ceiling", ceilVerts, ceilTris, ceilUvs);
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
            // A generated slab, not a scaled cube. The built-in cube maps each face
            // 0..1, so the same texture rendered on a 4.2 m wall and a 2.2 m wall had
            // visibly different grain -- one room, three sampling scales, and a
            // participant flagged the walls as "non-uniform material", which for a
            // study manipulating material type is a confound rather than a nitpick.
            // These UVs are in metres, matching the vault wall and caps, so every
            // surface in both shells shows the material at the same world scale.
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = centre;

            var mesh = SlabMesh(size);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = DefaultSurface();
            go.AddComponent<BoxCollider>().size = size;
            go.AddComponent<TintableSurface>();
        }

        /// <summary>A box with planar per-face UVs scaled in metres.</summary>
        static Mesh SlabMesh(Vector3 size)
        {
            var mesh = new Mesh { name = "Slab " + size.ToString() };
            var half = size / 2f;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Each face: outward normal n, and the face's two in-plane axes u/v whose
            // world lengths give the UV extents.
            AddFace(verts, uvs, tris, new Vector3(0, 0, -half.z), Vector3.right * half.x, Vector3.up * half.y);
            AddFace(verts, uvs, tris, new Vector3(0, 0, half.z), Vector3.left * half.x, Vector3.up * half.y);
            AddFace(verts, uvs, tris, new Vector3(-half.x, 0, 0), Vector3.forward * half.z, Vector3.up * half.y);
            AddFace(verts, uvs, tris, new Vector3(half.x, 0, 0), Vector3.back * half.z, Vector3.up * half.y);
            AddFace(verts, uvs, tris, new Vector3(0, half.y, 0), Vector3.right * half.x, Vector3.forward * half.z);
            AddFace(verts, uvs, tris, new Vector3(0, -half.y, 0), Vector3.right * half.x, Vector3.back * half.z);

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddFace(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                            Vector3 centre, Vector3 uAxis, Vector3 vAxis)
        {
            int b = verts.Count;
            verts.Add(centre - uAxis - vAxis);
            verts.Add(centre - uAxis + vAxis);
            verts.Add(centre + uAxis + vAxis);
            verts.Add(centre + uAxis - vAxis);

            float uLen = uAxis.magnitude * 2f, vLen = vAxis.magnitude * 2f;
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(0, vLen));
            uvs.Add(new Vector2(uLen, vLen));
            uvs.Add(new Vector2(uLen, 0));

            // Front face is the clockwise side seen from outside along the normal
            // (u cross v). The vault wall's winding bug is the cautionary tale here.
            tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
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
        /// <summary>Whether the rug takes the room's colour and material like the floor.</summary>
        public const bool RugFollowsDesign = true;

        static void AddFurniture(GameObject root, float depth)
        {
            var furniture = new GameObject("Fixed Furnishing");
            furniture.transform.SetParent(root.transform, false);

            float halfWidth = RoomDimensions.EntranceWidth / 2f;

            // Anchors. Real models and placeholders both land here, so swapping models
            // can never move furniture between conditions.
            var sofaAt      = new Vector3(0f, 0f, depth - 0.45f);
            var armchairAt  = new Vector3(-1.35f, 0f, depth - 1.45f);
            var tableAt     = new Vector3(0f, 0f, depth - 1.5f);
            var teacupAt    = new Vector3(0.18f, 0.4f, depth - 1.5f);
            var rugAt       = new Vector3(0f, 0.005f, depth - 1.2f);
            // Plan view in the schematic is drawn looking down with the participant at the
            // top facing the far wall, so page-left is the participant's RIGHT (+X) and
            // page-right is their LEFT (-X). Reading it the other way puts the door and
            // the bookcase on the wrong walls, which is what happened here.
            //
            // Bookcase and armchair: participant's left. Door and the second picture:
            // participant's right. Both side-wall items sit within the vestibule depth so
            // they exist in the curved shell too, where the side walls stop at 2.2 m.
            var shelfAt     = new Vector3(-(halfWidth - 0.22f), 0f, depth - 2.2f);
            var artAAt      = new Vector3(-0.3f, 1.55f, depth - 0.1f);
            var artBAt      = new Vector3(halfWidth - 0.08f, 1.55f, 1.15f);
            var doorAt      = new Vector3(halfWidth - 0.04f, 0f, 2.05f);

            if (!TryModel(furniture, "sofa", "Sofa", sofaAt, new Vector3(2.1f, 0.8f, 0.85f), 0f))
                BuildSofa(furniture, sofaAt);

            // -60 not +55: the imported models face -Z (their backrests sit at +Z, which
            // the OBJ vertex bounds confirm), so an armchair on the participant's left
            // needs to turn toward the table at +X. The old value was tuned for the box
            // placeholder and pointed the real model out at the wall.
            if (!TryModel(furniture, "armchair", "Armchair", armchairAt, new Vector3(0.85f, 0.8f, 0.85f), ArmchairYaw))
                BuildArmchair(furniture, armchairAt, ArmchairYaw);

            if (!TryModel(furniture, "coffeeTable", "Coffee Table", tableAt, new Vector3(1.1f, 0.4f, 0.6f), 0f))
                BuildCoffeeTable(furniture, tableAt);

            if (!TryModel(furniture, "teacup", "Teacup", teacupAt, new Vector3(0.1f, 0.09f, 0.1f), 0f))
                BuildTeacup(furniture, teacupAt);

            // The rug is a floor covering, so it follows the design like the floor does.
            //
            // Every other piece here is furnishing and stays neutral, which is what
            // keeps the manipulation off the sofa. The rug is the one piece that is not
            // furniture in that sense: it is 2.4 by 1.6 metres of floor, directly in
            // front of a seated participant and squarely in the middle of where they
            // look. Left neutral it is a grey patch covering an eighth of the floor area
            // the manipulation is carried on -- it does not protect the manipulation, it
            // dilutes it, in the part of the view that matters most.
            //
            // Set RugFollowsDesign false to put it back to neutral. It changes the
            // stimulus either way, so it is a decision rather than a detail.
            if (!TryModel(furniture, "rug", "Rug", rugAt, new Vector3(2.4f, 0.02f, 1.6f), 0f))
                AddBox(furniture, "Rug", rugAt + new Vector3(0f, 0.01f, 0f),
                    new Vector3(2.4f, 0.02f, 1.6f), 0f, 0.60f);

            if (RugFollowsDesign)
            {
                // Whichever of the two made it -- real model or placeholder box -- it is
                // named Rug and it is the only thing under here that is.
                var rug = furniture.transform.Find("Rug");
                if (rug != null)
                    foreach (var piece in rug.GetComponentsInChildren<Renderer>(true))
                        if (piece.GetComponent<TintableSurface>() == null)
                            piece.gameObject.AddComponent<TintableSurface>();
            }

            if (!TryModel(furniture, "bookshelf", "Bookshelf", shelfAt, new Vector3(0.9f, 1.8f, 0.35f), BookshelfYaw))
                BuildBookshelf(furniture, shelfAt, BookshelfYaw);

            // One picture on the far wall, one on the side wall by the door, as drawn.
            if (!TryModel(furniture, "wallArt", "Wall Art A", artAAt, new Vector3(0.7f, 0.5f, 0.05f), 0f))
                BuildWallArt(furniture, "Wall Art A", artAAt, 0f);
            if (!TryModel(furniture, "wallArt", "Wall Art B", artBAt, new Vector3(0.05f, 0.5f, 0.7f), SideWallYaw))
                BuildWallArt(furniture, "Wall Art B", artBAt, SideWallYaw);

            // The door. Closed and non-functional: it is there because a room with no way
            // in does not read as a room, and the schematic draws one. It is furnishing,
            // not a tintable surface, so the manipulation never touches it.
            BuildDoor(furniture, doorAt, SideWallYaw);
        }

        /// <summary>The furniture models to use, or null for procedural placeholders.</summary>
        public static FurnitureSet Models { get; set; }

        static bool TryModel(GameObject parent, string slot, string name, Vector3 at,
                             Vector3 footprint, float yaw)
        {
            if (Models == null) return false;
            var prefab = Models.For(slot);
            if (prefab == null) return false;

            var go = Object.Instantiate(prefab, parent.transform);
            go.name = name;
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            if (Models.normaliseToFootprint) FitTo(go, footprint);

            if (Models.forceNeutralMaterials)
            {
                var neutral = PropSurface(Models.neutralShade);
                foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                {
                    var slots = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < slots.Length; i++) slots[i] = neutral;
                    renderer.sharedMaterials = slots;
                }
            }

            // Furniture must never intercept the pointer ray meant for a panel or the
            // affect grid, and nothing here is physical.
            foreach (var collider in go.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(collider);

            return true;
        }

        /// <summary>
        /// Uniformly scale a model so its widest horizontal axis matches the placeholder
        /// footprint, then sit it on the floor. Uniform rather than per-axis so a model is
        /// never stretched, and driven by renderer bounds so it works whatever units the
        /// source was authored in.
        /// </summary>
        static void FitTo(GameObject go, Vector3 footprint)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.x <= 0f || bounds.size.z <= 0f) return;

            float scale = Mathf.Min(footprint.x / bounds.size.x, footprint.z / bounds.size.z);
            if (footprint.y > 0f && bounds.size.y > 0f)
                scale = Mathf.Min(scale, footprint.y / bounds.size.y);
            go.transform.localScale *= scale;

            // Re-measure, then move the model so its own centre lands on the anchor and
            // its base sits on the floor.
            //
            // Correcting height alone is not enough, which is what the first version did.
            // Asset-pack pivots are wherever the artist left them: Kenney's sofa spans
            // x[-0.98, 0], so its origin is at one END. Placed at x=0 the whole sofa sat
            // in the left half of the room, and every other piece was off by half its own
            // size in some direction. The layout looked scattered rather than like the
            // plan, and worse, it silently stopped matching the schematic the two shape
            // conditions are supposed to hold constant.
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 centre = go.transform.position;
            go.transform.position += new Vector3(
                centre.x - bounds.center.x,
                centre.y - bounds.min.y,
                centre.z - bounds.center.z);
        }

        // ------------------------------------------------------ procedural placeholders

        // Assembled from boxes rather than being one box each. Still placeholders, but a
        // sofa with arms and a back reads as a sofa, and a participant asked whether a
        // room feels calm should not first have to work out what they are looking at.

        static void BuildSofa(GameObject parent, Vector3 at)
        {
            var g = Group(parent, "Sofa", at, 0f);
            AddBox(g, "Seat",     new Vector3(0f, 0.32f, 0f),      new Vector3(2.1f, 0.22f, 0.85f), 0f, 0.34f);
            AddBox(g, "Back",     new Vector3(0f, 0.55f, -0.32f),  new Vector3(2.1f, 0.68f, 0.2f),  0f, 0.31f);
            AddBox(g, "Arm L",    new Vector3(-0.98f, 0.42f, 0f),  new Vector3(0.16f, 0.42f, 0.85f), 0f, 0.31f);
            AddBox(g, "Arm R",    new Vector3(0.98f, 0.42f, 0f),   new Vector3(0.16f, 0.42f, 0.85f), 0f, 0.31f);
            AddBox(g, "Cushion L", new Vector3(-0.63f, 0.46f, 0.03f), new Vector3(0.6f, 0.12f, 0.7f), 0f, 0.38f);
            AddBox(g, "Cushion M", new Vector3(0f, 0.46f, 0.03f),  new Vector3(0.6f, 0.12f, 0.7f),  0f, 0.38f);
            AddBox(g, "Cushion R", new Vector3(0.63f, 0.46f, 0.03f), new Vector3(0.6f, 0.12f, 0.7f), 0f, 0.38f);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 0.92f;
                float z = (i < 2 ? -1f : 1f) * 0.33f;
                AddBox(g, "Foot " + i, new Vector3(x, 0.1f, z), new Vector3(0.07f, 0.2f, 0.07f), 0f, 0.2f);
            }
        }

        static void BuildArmchair(GameObject parent, Vector3 at, float yaw)
        {
            var g = Group(parent, "Armchair", at, yaw);
            AddBox(g, "Seat",  new Vector3(0f, 0.32f, 0f),     new Vector3(0.8f, 0.22f, 0.8f), 0f, 0.40f);
            AddBox(g, "Back",  new Vector3(0f, 0.58f, -0.3f),  new Vector3(0.8f, 0.72f, 0.18f), 0f, 0.37f);
            AddBox(g, "Arm L", new Vector3(-0.35f, 0.44f, 0f), new Vector3(0.14f, 0.42f, 0.8f), 0f, 0.37f);
            AddBox(g, "Arm R", new Vector3(0.35f, 0.44f, 0f),  new Vector3(0.14f, 0.42f, 0.8f), 0f, 0.37f);
            AddBox(g, "Cushion", new Vector3(0f, 0.46f, 0.02f), new Vector3(0.62f, 0.12f, 0.66f), 0f, 0.44f);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 0.32f;
                float z = (i < 2 ? -1f : 1f) * 0.32f;
                AddBox(g, "Foot " + i, new Vector3(x, 0.1f, z), new Vector3(0.07f, 0.2f, 0.07f), 0f, 0.2f);
            }
        }

        static void BuildCoffeeTable(GameObject parent, Vector3 at)
        {
            var g = Group(parent, "Coffee Table", at, 0f);
            AddBox(g, "Top",   new Vector3(0f, 0.38f, 0f), new Vector3(1.1f, 0.05f, 0.6f), 0f, 0.47f);
            AddBox(g, "Shelf", new Vector3(0f, 0.12f, 0f), new Vector3(0.95f, 0.03f, 0.5f), 0f, 0.44f);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 0.5f;
                float z = (i < 2 ? -1f : 1f) * 0.25f;
                AddBox(g, "Leg " + i, new Vector3(x, 0.19f, z), new Vector3(0.06f, 0.38f, 0.06f), 0f, 0.4f);
            }
        }

        static void BuildTeacup(GameObject parent, Vector3 at)
        {
            var g = Group(parent, "Teacup", at, 0f);
            AddCylinder(g, "Saucer", new Vector3(0f, 0.006f, 0f), new Vector3(0.14f, 0.006f, 0.14f), 0.9f);
            AddCylinder(g, "Cup",    new Vector3(0f, 0.045f, 0f), new Vector3(0.08f, 0.04f, 0.08f), 0.92f);
            AddBox(g, "Handle", new Vector3(0.055f, 0.05f, 0f), new Vector3(0.025f, 0.03f, 0.012f), 0f, 0.92f);
        }

        static void BuildBookshelf(GameObject parent, Vector3 at, float yaw)
        {
            var g = Group(parent, "Bookshelf", at, yaw);
            AddBox(g, "Side L", new Vector3(0f, 0.9f, -0.44f), new Vector3(0.35f, 1.8f, 0.03f), 0f, 0.36f);
            AddBox(g, "Side R", new Vector3(0f, 0.9f, 0.44f),  new Vector3(0.35f, 1.8f, 0.03f), 0f, 0.36f);
            AddBox(g, "Back",   new Vector3(0.16f, 0.9f, 0f),  new Vector3(0.03f, 1.8f, 0.9f),  0f, 0.33f);

            var rng = new System.Random(7);   // fixed seed: identical in every room
            for (int shelf = 0; shelf < 5; shelf++)
            {
                float y = 0.12f + shelf * 0.4f;
                AddBox(g, "Shelf " + shelf, new Vector3(0f, y, 0f), new Vector3(0.35f, 0.03f, 0.88f), 0f, 0.43f);

                float z = -0.4f;
                while (z < 0.36f)
                {
                    float w = 0.03f + (float)rng.NextDouble() * 0.03f;
                    float h = 0.2f + (float)rng.NextDouble() * 0.1f;
                    AddBox(g, "Book", new Vector3(0f, y + 0.015f + h / 2f, z + w / 2f),
                        new Vector3(0.26f, h, w), 0f, 0.3f + (float)rng.NextDouble() * 0.4f);
                    z += w + 0.004f;
                }
            }
        }

        /// <summary>Armchair turned toward the coffee table. See AddFurniture.</summary>
        const float ArmchairYaw = -60f;

        /// <summary>Backs onto the participant's left wall, opening into the room.</summary>
        const float BookshelfYaw = -90f;

        /// <summary>Flat against the participant's right wall, facing into the room.</summary>
        const float SideWallYaw = -90f;

        static void BuildDoor(GameObject parent, Vector3 at, float yaw)
        {
            var g = Group(parent, "Door", at, yaw);
            AddBox(g, "Leaf",    new Vector3(0f, 1.0f, 0f),   new Vector3(0.86f, 2.0f, 0.04f), 0f, 0.55f);
            AddBox(g, "Frame L", new Vector3(-0.47f, 1.03f, 0f), new Vector3(0.08f, 2.06f, 0.06f), 0f, 0.3f);
            AddBox(g, "Frame R", new Vector3(0.47f, 1.03f, 0f),  new Vector3(0.08f, 2.06f, 0.06f), 0f, 0.3f);
            AddBox(g, "Frame Top", new Vector3(0f, 2.03f, 0f), new Vector3(1.02f, 0.06f, 0.06f), 0f, 0.3f);
            AddBox(g, "Handle",  new Vector3(0.34f, 1.05f, -0.04f), new Vector3(0.12f, 0.03f, 0.05f), 0f, 0.75f);
        }

        static void BuildWallArt(GameObject parent, string name, Vector3 at, float yaw)
        {
            var g = Group(parent, name, at, yaw);
            AddBox(g, "Frame",  new Vector3(0f, 0f, -0.01f), new Vector3(0.7f, 0.5f, 0.04f), 0f, 0.25f);
            AddBox(g, "Canvas", new Vector3(0f, 0f, -0.035f), new Vector3(0.62f, 0.42f, 0.01f), 0f, 0.78f);
        }

        static GameObject Group(GameObject parent, string name, Vector3 at, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        static void AddBox(GameObject parent, string name, Vector3 centre, Vector3 size,
                           float yaw, float shade)
        {
            AddProp(parent, name, centre, size, yaw, shade);
        }

        static void AddCylinder(GameObject parent, string name, Vector3 centre, Vector3 size,
                                float shade)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = centre;
            // Unity's cylinder is 2 units tall, so halve the Y scale to get the real height.
            go.transform.localScale = new Vector3(size.x, size.y / 2f, size.z);
            go.GetComponent<Renderer>().sharedMaterial = PropSurface(shade);
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        static void AddProp(GameObject parent, string name, Vector3 centre, Vector3 size,
                            float yaw = 0f, float shade = 0.5f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = PropSurface(shade);

            // Nothing in the study is physical, and a furniture collider nearer than a
            // question panel would intercept the pointer ray meant for it.
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        static readonly Dictionary<float, Material> propSurfaces = new Dictionary<float, Material>();

        /// <summary>One shared material per shade, so eight props do not become eight
        /// material instances leaking on every room rebuild.</summary>
        static Material PropSurface(float shade)
        {
            Material cached;
            if (propSurfaces.TryGetValue(shade, out cached) && cached != null) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "Greybox Prop " + shade.ToString("0.00") };
            var colour = new Color(shade, shade, shade);
            // _BaseColor is URP, _Color built-in. Set whichever the shader actually has
            // rather than assuming a pipeline.
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
            // Furnishing should not read as polished plastic next to matte walls.
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);

            propSurfaces[shade] = material;
            return material;
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

}
