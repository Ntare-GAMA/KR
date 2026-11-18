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
        [Tooltip("Every named place in the play area as mapped in OpenStreetMap (shops, cafes, pharmacies, banks...): " +
                 "satnav destinations and signboards. Names exactly as in OSM; nothing invented.")]
        public DirectoryEntry[] directory = new DirectoryEntry[0];

        public PlaceRecord Find(string id)
        {
            foreach (var p in places)
                if (p.id == id)
                    return p;
            return null;
        }
    }

    [System.Serializable]
    public sealed class DirectoryEntry
    {
        public string name;
        public string kind;     // cafe, pharmacy, bank...
        public string group;    // food, shop, health, finance, lodging, service, education, worship, leisure, transport, office, building
        public string street;   // addr:street when mapped
        public Vector3 position;
        public string osm;      // node/123 or way/456
    }
}
