// Records which build of the setup code produced this scene.
//
// The scene is generated, so it goes stale silently: the code gains a component and the
// scene in front of you does not have it, with no error and nothing missing that Unity
// would complain about. The failures that produces are the confusing kind -- forms that
// never appear because the server was never added, a rating grid that never moves
// because the field that tells it to did not exist when the scene was built. Both look
// like the study is broken rather than out of date.
//
// Bump Current whenever Set Up Study Scene starts producing something a previously built
// scene would not have. The control panel compares the two and refuses to start a session
// on a stale scene.

using UnityEngine;

namespace EmotionRooms
{
    public class StudySceneStamp : MonoBehaviour
    {
        /// <summary>Raise this when the setup code changes what it builds.</summary>
        public const int Current = 41;

        [Tooltip("Set by Emotion Rooms > Study Control Panel when the scene is built. " +
                 "Do not edit by hand: a stamp that disagrees with the scene is worse " +
                 "than no stamp, because it silences the warning that would have caught it.")]
        public int version;

        [Tooltip("What changed in this version, so a stale-scene warning can say why it matters.")]
        public string note = "";

        public bool IsCurrent { get { return version == Current; } }
    }
}
