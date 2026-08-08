// The neutral ground the participant stands on while rating.
//
// The room is hidden before the grid appears, so a rating measures how the room made
// someone feel rather than how it looks while they study it. Hiding it left nothing at
// all: no floor, no horizon, a body floating in empty space. That is unpleasant enough to
// be a confound in a study whose dependent variable is how you feel, and it made people
// think the app had broken.
//
// So: a floor and a distant surround, deliberately affect-free. Mid-grey at neutral white,
// no texture, no hue, no shadow gradient worth reading into. It has to be *somewhere*
// without being anywhere in particular.

using UnityEngine;

namespace EmotionRooms
{
    public class RatingStage : MonoBehaviour
    {
        [Tooltip("Radius of the floor disc, metres. Wide enough that its edge is not a " +
                 "feature the participant is standing near.")]
        public float radius = 6f;

        [Tooltip("Height of the surround. Not a room: it is far enough away and plain " +
                 "enough to read as absence rather than as another room to be rated.")]
        public float wallHeight = 4f;

        /// <summary>Neutral by construction: equal RGB, so it carries no hue at all.</summary>
        public static readonly Color Ground = new Color(0.34f, 0.34f, 0.34f);
        public static readonly Color Surround = new Color(0.46f, 0.46f, 0.46f);

        Light ambient;

        void Awake()
        {
            if (transform.childCount == 0) Build();
            // No Hide() -- the deferred-Awake trap: on an instance saved inactive, Awake
            // runs inside the first Show() and a Hide() here would cancel it. The stage
            // is created active and shown immediately, so there is nothing to hide.
        }

        public void Show()
        {
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        void Build()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Rating Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
            floor.transform.localPosition = new Vector3(0f, -0.025f, 0f);
            Paint(floor, shader, Ground);
            // No collider: locomotion is bounded by Locomotion.Clamp, and a collider here
            // would only give the pointer something else to hit in front of the grid.
            Strip(floor);

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wall.name = "Rating Surround";
            wall.transform.SetParent(transform, false);
            wall.transform.localScale = new Vector3(radius * 2.4f, wallHeight * 0.5f, radius * 2.4f);
            wall.transform.localPosition = new Vector3(0f, wallHeight * 0.5f, 0f);
            Paint(wall, shader, Surround);
            Strip(wall);
            // Seen from the inside, so the outward-facing hull has to be flipped.
            Invert(wall);

            var lightObject = new GameObject("Rating Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, wallHeight, 0f);
            ambient = lightObject.AddComponent<Light>();
            ambient.type = LightType.Point;
            ambient.range = radius * 4f;
            ambient.intensity = 1.1f;
            // 4500K, the study's neutral white. The rating environment must not tilt warm
            // or cool: that is one of the manipulated variables.
            ambient.color = new Color(1f, 0.96f, 0.91f);
            ambient.shadows = LightShadows.None;
        }

        static void Paint(GameObject target, Shader shader, Color colour)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            var material = new Material(shader) { name = target.name };
            material.color = colour;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.05f);
            renderer.sharedMaterial = material;
        }

        static void Strip(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
        }

        static void Invert(GameObject target)
        {
            var filter = target.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;

            var mesh = Instantiate(filter.sharedMesh);
            mesh.name = filter.sharedMesh.name + " (inverted)";
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetTriangles(sub);
                System.Array.Reverse(indices);
                mesh.SetTriangles(indices, sub);
            }
            var normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++) normals[i] = -normals[i];
            mesh.normals = normals;
            filter.sharedMesh = mesh;
        }
    }
}
