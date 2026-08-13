// One MonoBehaviour, one file, named the same. Not a style rule -- Unity cannot
// build a MonoScript for a MonoBehaviour whose class name does not match its file
// name, and without a MonoScript the component cannot be written into a scene.
//
// This class used to live at the bottom of RoomBuilder.cs. It got away with it
// because nothing needs it to survive a save: scene setup adds it, collects the
// renderers in the same pass, and stores those renderers instead. The identical
// arrangement in Questionnaire.cs did not get away with it, and cost days -- see
// the commit that renamed it.

using UnityEngine;

namespace EmotionRooms
{
    /// <summary>
    /// Marks a surface whose colour and roughness the config drives. Walls, floors and
    /// ceilings carry this; furniture deliberately does not.
    /// </summary>
    public class TintableSurface : MonoBehaviour { }
}
