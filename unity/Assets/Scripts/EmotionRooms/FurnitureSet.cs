// Optional real models for the fixed furnishing.
//
// The furnishing is identical in every room and every emotion, so it is not part of the
// manipulation -- but it is part of whether a participant believes they are standing in a
// room, and eight grey boxes do not read as a living room. This lets real models replace
// the procedural placeholders without touching RoomBuilder.
//
// Leave any slot empty and that piece falls back to the procedural version, so the study
// still runs on a machine that has not imported the models.
//
// Kenney's Furniture Kit (kenney.nl/assets/furniture-kit) is CC0: no attribution
// required, no licence to clear with the university, and it covers every slot here.
//
// Setup: Assets > Create > Emotion Rooms > Furniture Set, drop the prefabs in, then
// assign the asset in the Study Control Panel.
//
// Models are placed at the same anchors as the placeholders and uniformly scaled to the
// same footprint, so swapping them cannot move furniture between conditions -- which
// would turn a cosmetic change into a confound.

using UnityEngine;

namespace EmotionRooms
{
    [CreateAssetMenu(menuName = "Emotion Rooms/Furniture Set", fileName = "FurnitureSet")]
    public class FurnitureSet : ScriptableObject
    {
        [Tooltip("Three-seat sofa, centred against the far wall.")]
        public GameObject sofa;

        [Tooltip("Single armchair, offset and angled toward the table.")]
        public GameObject armchair;

        [Tooltip("Coffee table in front of the sofa.")]
        public GameObject coffeeTable;

        [Tooltip("Teacup, sits on the coffee table.")]
        public GameObject teacup;

        [Tooltip("Rug under the table.")]
        public GameObject rug;

        [Tooltip("Bookshelf against a side wall.")]
        public GameObject bookshelf;

        [Tooltip("Wall art. Used for both pieces.")]
        public GameObject wallArt;

        [Tooltip("Scale models to the placeholder footprint. Leave on: it keeps the " +
                 "furnishing identical across conditions regardless of how the source " +
                 "models were authored.")]
        public bool normaliseToFootprint = true;

        public GameObject For(string slot)
        {
            switch (slot)
            {
                case "sofa": return sofa;
                case "armchair": return armchair;
                case "coffeeTable": return coffeeTable;
                case "teacup": return teacup;
                case "rug": return rug;
                case "bookshelf": return bookshelf;
                case "wallArt": return wallArt;
                default: return null;
            }
        }

        /// <summary>Slots still on the procedural placeholder.</summary>
        public int MissingCount()
        {
            string[] slots = { "sofa", "armchair", "coffeeTable", "teacup", "rug",
                               "bookshelf", "wallArt" };
            int missing = 0;
            foreach (var slot in slots) if (For(slot) == null) missing++;
            return missing;
        }
    }
}
