// Readable text on an object in the scene.
//
// Every question in this study was being asked with unlabelled grey cubes: the panels
// carried a `prompt` string that nothing rendered, and the option buttons were named only
// in the hierarchy. A participant could see two boxes and had no way to know the room was
// meant to feel calm, which of the boxes meant yes, or what they were being asked at all.
//
// TextMesh rather than TextMeshPro: no package import, no font asset to keep in the
// build, and it renders in an immersive view, which canvas-based UI does not without an
// event camera and a raycaster wired correctly.

using UnityEngine;

namespace EmotionRooms
{
    public static class WorldLabel
    {
        /// <summary>Attach text to a transform, sized in metres of character height.</summary>
        public static TextMesh Attach(Transform parent, string text, float height = 0.05f,
                                      Vector3 offset = default(Vector3),
                                      TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.identity;

            // The parent may be a scaled primitive -- the panel cells are flattened cubes
            // -- and inherited scale would squash the text with it.
            var lossy = parent.lossyScale;
            go.transform.localScale = new Vector3(
                lossy.x > 0.0001f ? 1f / lossy.x : 1f,
                lossy.y > 0.0001f ? 1f / lossy.y : 1f,
                lossy.z > 0.0001f ? 1f / lossy.z : 1f);

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            // TextMesh's characterSize is not a height: rendered line height is roughly
            // characterSize * fontSize / 10. Passing metres straight into characterSize
            // made every label ten times too large -- half-metre letters filling the
            // room. Solve for characterSize so `height` really is metres.
            mesh.fontSize = 96;
            mesh.characterSize = height * 10f / mesh.fontSize;
            mesh.anchor = anchor;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.97f, 0.97f, 0.99f);

            // Drawn after the surface it sits on, so it is never swallowed by it.
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 200;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return mesh;
        }

        /// <summary>Break a long prompt so it does not run off the panel.</summary>
        public static string Wrap(string text, int perLine = 34)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var wrapped = new System.Text.StringBuilder();
            int since = 0;
            foreach (var word in text.Split(' '))
            {
                if (since > 0 && since + word.Length > perLine)
                {
                    wrapped.Append('\n');
                    since = 0;
                }
                else if (since > 0)
                {
                    wrapped.Append(' ');
                    since++;
                }
                wrapped.Append(word);
                since += word.Length;
            }
            return wrapped.ToString();
        }
    }
}
