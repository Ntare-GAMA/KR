using UnityEngine;

namespace KR.Gameplay.Story
{
    // The places you can discover in Kimironko, Kibagabaga and the ALU campus (public places only: markets,
    // parks, schools, hospitals, bus stations, sports grounds, places of worship and the story's own places;
    // private businesses are left out). Built from the OSM place list; card text is written and reviewed by a person.
    [CreateAssetMenu(menuName = "Kigali Rush/Story/Place Catalog")]
    public sealed class PlaceCatalog : ScriptableObject
    {
        public PlaceRecord[] places = new PlaceRecord[0];
        [Tooltip("Place ids of the Kimironko tour, in order.")] public string[] tour = new string[0];

        public PlaceRecord Find(string id)
        {
            foreach (var p in places)
                if (p.id == id)
                    return p;
            return null;
        }
    }
}
