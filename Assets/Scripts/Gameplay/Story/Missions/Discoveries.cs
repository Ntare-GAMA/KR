using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Discovering places: come within a place's radius (on foot or by car) and it is added to your journal, once,
    // with its XP. While driving it only shows as one quiet line on the dashboard; the card is read in the journal.
    // Finishing a sector earns its badge. Checks four times a second against a few dozen places: negligible cost.
    public sealed class Discoveries : MonoBehaviour
    {
        [SerializeField] PlaceCatalog catalog;
        [SerializeField] StoryDirector director;
        [SerializeField] Navigator navigator;

        float _timer;

        public PlaceCatalog Catalog => catalog;

        public void Configure(PlaceCatalog places, StoryDirector story, Navigator nav)
        {
            catalog = places;
            director = story;
            navigator = nav;
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f || catalog == null || director == null || navigator == null)
                return;
            _timer = 0.25f;
            Vector3 p = navigator.PlayerPosition;
            foreach (var place in catalog.places)
            {
                if (director.IsDiscovered(place.id))
                    continue;
                float dx = place.position.x - p.x, dz = place.position.z - p.z;
                if (dx * dx + dz * dz > place.radius * place.radius)
                    continue;
                director.Discover(place);
                CheckSector(place.sector);
            }
        }

        void CheckSector(string sector)
        {
            string badge = sector == "Kimironko" ? "kimironko" : sector == "Kibagabaga" ? "kibagabaga" : null;
            if (badge == null)
                return;
            foreach (var place in catalog.places)
                if (place.sector == sector && !director.IsDiscovered(place.id))
                    return;
            director.Badge(badge);
        }

        // Discovered / total for one sector, for the journal.
        public (int found, int total) Count(string sector)
        {
            int found = 0, total = 0;
            foreach (var place in catalog.places)
                if (place.sector == sector)
                {
                    total++;
                    if (director.IsDiscovered(place.id))
                        found++;
                }
            return (found, total);
        }

        public IEnumerable<PlaceRecord> InSector(string sector)
        {
            foreach (var place in catalog.places)
                if (place.sector == sector)
                    yield return place;
        }
    }
}
