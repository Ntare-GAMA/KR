using UnityEngine;

namespace KR.Gameplay.Story
{
    public enum VenueKind { None, Bank, Clinic, Hotel, Restaurant, School, Shop, Pharmacy, Police, Market, Office }

    // What kind of place a name and map group describe, and the look of conversations held there. Colours
    // only, no logos.
    public static class Venue
    {
        public struct Theme
        {
            public Color tab, tabDark, accent, panel, row;
            public string label;      // shown above the place name: BANK, CLINIC...
        }

        public static VenueKind Classify(string name, string group)
        {
            string n = (name ?? "").ToLowerInvariant();
            string g = (group ?? "").ToLowerInvariant();
            if (Has(n, "bank", "banque", "sacco", "microfinance", "forex", "bureau de change") || g == "finance")
                return VenueKind.Bank;
            if (Has(n, "pharmac"))
                return VenueKind.Pharmacy;
            if (Has(n, "clinic", "clinique", "hospital", "hôpital", "health", "medical", "doctor", "dental", "polyclinic", "centre de santé") || g == "health")
                return VenueKind.Clinic;
            if (Has(n, "hotel", "hôtel", "lodge", "guest house", "guesthouse", "motel", "inn", "apartments") || g == "lodging")
                return VenueKind.Hotel;
            if (Has(n, "restaurant", "café", "cafe", "coffee", "bar ", "grill", "bistro", "kitchen", "pizza") || g == "food")
                return VenueKind.Restaurant;
            if (Has(n, "university", "school", "college", "academy", "alu", "ecole", "école") || g == "education")
                return VenueKind.School;
            if (Has(n, "police"))
                return VenueKind.Police;
            if (Has(n, "market", "isoko"))
                return VenueKind.Market;
            if (g == "office" || g == "service")
                return VenueKind.Office;
            if (g == "shop")
                return VenueKind.Shop;
            return VenueKind.None;
        }

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words)
                if (s.Contains(w))
                    return true;
            return false;
        }

        static Color C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

        public static Theme ThemeFor(VenueKind kind)
        {
            switch (kind)
            {
                case VenueKind.Bank:       return new Theme { label = "BANK", tab = C(18, 72, 140), tabDark = C(8, 30, 62), accent = C(240, 196, 80), panel = C(6, 16, 34, 238), row = C(14, 30, 58, 228) };
                case VenueKind.Clinic:     return new Theme { label = "CLINIC", tab = C(0, 150, 136), tabDark = C(0, 70, 64), accent = C(255, 255, 255), panel = C(6, 32, 34, 238), row = C(12, 52, 54, 228) };
                case VenueKind.Pharmacy:   return new Theme { label = "PHARMACY", tab = C(34, 160, 70), tabDark = C(12, 70, 30), accent = C(220, 255, 230), panel = C(6, 28, 14, 238), row = C(14, 48, 26, 228) };
                case VenueKind.Hotel:      return new Theme { label = "HOTEL", tab = C(110, 40, 120), tabDark = C(50, 14, 56), accent = C(236, 200, 120), panel = C(24, 8, 28, 238), row = C(44, 18, 50, 228) };
                case VenueKind.Restaurant: return new Theme { label = "RESTAURANT", tab = C(214, 90, 30), tabDark = C(96, 36, 8), accent = C(255, 214, 150), panel = C(34, 14, 6, 238), row = C(58, 26, 12, 228) };
                case VenueKind.School:     return new Theme { label = "CAMPUS", tab = C(170, 30, 45), tabDark = C(70, 8, 16), accent = C(255, 200, 87), panel = C(26, 8, 12, 238), row = C(46, 16, 22, 228) };
                case VenueKind.Police:     return new Theme { label = "POLICE", tab = C(24, 52, 110), tabDark = C(10, 22, 50), accent = C(255, 220, 60), panel = C(8, 12, 28, 238), row = C(18, 26, 50, 228) };
                case VenueKind.Market:     return new Theme { label = "MARKET", tab = C(230, 140, 20), tabDark = C(100, 56, 4), accent = C(120, 220, 90), panel = C(30, 18, 4, 238), row = C(54, 34, 10, 228) };
                case VenueKind.Shop:       return new Theme { label = "SHOP", tab = C(200, 60, 110), tabDark = C(84, 20, 44), accent = C(255, 230, 120), panel = C(30, 8, 18, 238), row = C(52, 18, 32, 228) };
                case VenueKind.Office:     return new Theme { label = "OFFICE", tab = C(70, 84, 100), tabDark = C(30, 38, 48), accent = C(120, 210, 255), panel = C(14, 18, 24, 238), row = C(28, 34, 44, 228) };
                default:                   return new Theme { label = "", tab = UI.KrTheme.Red, tabDark = UI.KrTheme.RedDeep, accent = UI.KrTheme.Accent, panel = UI.KrTheme.Ink, row = UI.KrTheme.InkSoft };
            }
        }

        // The named place nearest to p within radius (from the satnav's list of OSM places), and its kind.
        public static VenueKind Near(Navigator nav, Vector3 p, float radius, out string name)
        {
            name = null;
            if (nav == null)
                return VenueKind.None;
            float best = radius * radius;
            int pick = -1;
            var places = nav.Places;
            for (int i = 0; i < places.Count; i++)
            {
                var d = places[i].pos - p;
                d.y = 0f;
                if (d.sqrMagnitude < best)
                {
                    best = d.sqrMagnitude;
                    pick = i;
                }
            }
            if (pick < 0)
                return VenueKind.None;
            name = places[pick].name;
            return Classify(name, nav.GroupOf(pick));
        }
    }
}
