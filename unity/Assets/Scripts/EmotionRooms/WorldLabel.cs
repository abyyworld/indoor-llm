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
        /// <summary>
        /// Amber, not white.
        /// </summary>
        ///
        /// White text plus a black outline still failed against a 750 lux wall: a bright
        /// wall in this study is a pale, low-saturation surface, so white-on-it is white
        /// on almost-white and the outline is the only thing carrying the glyph. Amber
        /// differs from every wall the pools can produce in hue as well as luminance,
        /// which is what makes it legible rather than merely present - the pools top out
        /// at 40% saturation, so no wall can approach this chroma. With the black
        /// outline behind it, it reads on the dim rooms too.
        public static readonly Color Ink = new Color(1f, 0.82f, 0.28f);

        /// <summary>
        /// A mesh object built without GameObject.CreatePrimitive.
        ///
        /// CreatePrimitive attaches a collider, and the collider class is whatever the
        /// primitive implies -- a Cylinder wants a CapsuleCollider. With engine stripping
        /// on, a collider class nothing in the saved scene uses is stripped from the
        /// build, and every such primitive logs an error at spawn. Preserving the class
        /// through link.xml turned out worse: builds carrying that preserve died during
        /// scene load. So runtime construction takes the mesh directly from the built-in
        /// resources and attaches no collider at all -- every caller here was destroying
        /// the collider anyway.
        /// </summary>
        public static GameObject Solid(string meshResource, string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>(meshResource);
            go.AddComponent<MeshRenderer>();
            return go;
        }


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
            mesh.color = Ink;

            // Drawn after the surface it sits on, so it is never swallowed by it, and
            // out of the lighting entirely: a label lit by the room is a label whose
            // legibility changes with the stimulus, which is the whole complaint. Text
            // has to read identically at 150 lux and at 750.
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 200;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            Plate(go.transform, text, height);

            Outline(go.transform, mesh, height);
            return mesh;
        }

        /// <summary>
        /// A black outline behind the glyphs, so white text stays legible on anything.
        ///
        /// White-on-bright was unreadable in a 750 lux room and white-on-dark is the
        /// only thing that works in a dim one, so no single colour can serve: the study
        /// deliberately varies wall brightness across nearly two orders of magnitude,
        /// and text has to survive all of it. Four offset copies in black behind the
        /// glyphs give a cheap outline that does. TextMesh has no outline of its own and
        /// pulling in TextMeshPro for it would mean a font asset to keep in the project.
        /// </summary>
        /// <summary>
        /// Change a label's text, outline included.
        ///
        /// Anything whose wording changes at runtime - the question prompt, the message
        /// board - has to go through here. Setting .text directly leaves the four
        /// outline copies showing the previous sentence behind the new one.
        /// </summary>
        public static void SetText(TextMesh label, string value)
        {
            if (label == null) return;
            label.text = value;

            foreach (Transform child in label.transform.parent)
            {
                if (child.name != "Outline") continue;
                var copy = child.GetComponent<TextMesh>();
                if (copy != null) copy.text = value;
            }
        }

        /// <summary>
        /// A dark plate behind the glyphs, sized to the text.
        ///
        /// The outline alone still lost against a bright wall: an outline only separates
        /// a glyph from what is immediately around it, and a pale 750 lux wall behind
        /// thin strokes still swamps them. A plate replaces the background instead of
        /// fighting it, so contrast is fixed by us rather than by the stimulus.
        /// </summary>
        static void Plate(Transform parent, string text, float height)
        {
            int longest = 0, lines = 1;
            int run = 0;
            foreach (char c in text ?? "")
            {
                if (c == '\n') { lines++; if (run > longest) longest = run; run = 0; }
                else run++;
            }
            if (run > longest) longest = run;
            if (longest == 0) return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Plate";
            quad.transform.SetParent(parent, false);
            // Behind the glyphs in the same local space, with a margin round the text.
            quad.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            quad.transform.localScale = new Vector3(longest * height * 0.62f + height * 0.5f,
                                                    lines * height * 1.45f, 1f);

            // DestroyImmediate outside play mode, Destroy inside it.
            //
            // Object.Destroy is deferred to the end of the frame, and in the editor
            // there is no frame: it silently does nothing. Scene setup runs in the
            // editor, so every one of these plates kept a MeshCollider it was never
            // meant to have - 41 of them, saved into the scene. They sit between the
            // participant and the buttons, so the pointer would hit a plate instead of
            // the option behind it, and they are the bulk of what the scene gained
            // since the last build verified on device.
            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null && shader != null)
            {
                renderer.material = new Material(shader) { color = new Color(0.05f, 0.05f, 0.07f) };
                renderer.sortingOrder = 198;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
        }

        static void Outline(Transform parent, TextMesh source, float height)
        {
            // Scaled from the glyph height so the outline is proportional rather than
            // hairline on big text and a blob on small.
            float step = height * 0.06f;
            var offsets = new[]
            {
                new Vector3(step, 0f, 0.001f), new Vector3(-step, 0f, 0.001f),
                new Vector3(0f, step, 0.001f), new Vector3(0f, -step, 0.001f),
            };

            foreach (var offset in offsets)
            {
                var edge = new GameObject("Outline");
                edge.transform.SetParent(parent, false);
                edge.transform.localPosition = offset;
                edge.transform.localRotation = Quaternion.identity;

                var copy = edge.AddComponent<TextMesh>();
                copy.text = source.text;
                copy.font = source.font;
                copy.fontSize = source.fontSize;
                copy.characterSize = source.characterSize;
                copy.anchor = source.anchor;
                copy.alignment = source.alignment;
                copy.color = new Color(0f, 0f, 0f, 0.95f);

                var edgeRenderer = edge.GetComponent<MeshRenderer>();
                if (edgeRenderer != null)
                {
                    edgeRenderer.sharedMaterial = source.GetComponent<MeshRenderer>().sharedMaterial;
                    // Behind the white glyphs, still in front of the surface.
                    edgeRenderer.sortingOrder = 199;
                    edgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    edgeRenderer.receiveShadows = false;
                }
            }
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
