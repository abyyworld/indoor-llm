// Every pool value in words a participant already owns, in one place.
//
// These words existed twice: once in StudySceneSetup, which paints the buttons, and
// once in pipeline/rationales.py, which writes what the system says about a room. They
// drifted -- the system said "bare concrete" while the button said "stone-like wall" --
// and a participant asked whether the reasoning matched the room had to translate before
// they could answer. That judgement is the measure the explanation manipulation exists
// to produce, so the mismatch landed on it as noise.
//
// The Python side is still a separate copy, because it has to run without Unity. A test
// keeps the two in step. This file removes the second copy, which was about to appear
// the moment anything at runtime needed to name a value.

namespace EmotionRooms
{
    public static class PlainWords
    {
        /// <summary>The variable, as the attribution buttons name it.</summary>
        public static string Field(string field)
        {
            switch (field)
            {
                case "hue": return "the colour";
                case "saturation": return "colour strength";
                case "texture": return "wall material";
                case "material": return "wall material";
                case "nothing_wrong": return "nothing was changed";
                default: return field;
            }
        }

        /// <summary>
        /// The variable inside "What should ___ be instead?", so it needs an article.
        /// </summary>
        public static string FieldInSentence(string field)
        {
            switch (field)
            {
                case "hue": return "the colour";
                case "saturation": return "the colour strength";
                case "brightness": return "the brightness";
                case "texture": return "the wall material";
                case "roughness": return "the roughness";
                case null: return "it";
                default: return "the " + field;
            }
        }

        /// <summary>
        /// A pool value in words. Values collide across fields -- 300 is both a hue and
        /// an illuminance -- so the field has to come with it.
        /// </summary>
        public static string Value(string field, string value)
        {
            switch (field)
            {
                case "hue":
                    // Names checked against what the renderer actually produces at the
                    // study's saturations, not against the angle's textbook name. 270 is
                    // #ad82d8, which nobody calls blue-violet, and 300 is #d882d8, which
                    // is magenta rather than purple. Two hues were competing for "purple"
                    // and the nearer one was not getting it.
                    switch (value)
                    {
                        case "0": return "red";
                        case "30": return "orange";
                        case "60": return "yellow";
                        case "90": return "yellow-green";
                        case "120": return "green";
                        case "180": return "blue-green";
                        case "240": return "blue";
                        case "270": return "purple";
                        case "300": return "magenta";
                        case "330": return "pink";
                    }
                    break;
                case "saturation":
                    if (value == "0.2") return "faint";
                    if (value == "0.4") return "vivid";
                    break;
                case "brightness":
                    if (value == "150") return "dim";
                    if (value == "300") return "medium";
                    if (value == "500") return "bright";
                    if (value == "750") return "very bright";
                    break;
                case "texture":
                    if (value == "plaster") return "painted plaster";
                    if (value == "concrete") return "bare concrete";
                    if (value == "textile") return "woven cloth";
                    break;
            }
            return value;   // rough and smooth are already words
        }
    }
}
