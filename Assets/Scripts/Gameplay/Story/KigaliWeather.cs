using System;
using System.Collections;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.Networking;

namespace KR.Gameplay.Story
{
    public struct WeatherNow
    {
        public bool Live;            // true = fetched from the forecast service, false = offline fallback
        public float TemperatureC;
        public float WindMs;
        public float CloudPercent;
        public float RainMmNextHour;
        public string Symbol;        // MET Norway symbol code, e.g. "lightrain_showers_day"
        public DateTime FetchedKigali;
    }

    // Session weather (default): the first session is sunny and every new session (each launch) brings the next
    // weather in the list, held for the whole session. Live mode (optional) uses Kigali weather from MET Norway's
    // Locationforecast API (free, commercial use allowed with attribution,
    // requires an identifying User-Agent; TODO: verify current terms at api.met.no). Offline or on any error it
    // falls back to a simple seasonal pattern, so the game never waits on the network and never throws.
    public sealed class KigaliWeather : MonoBehaviour
    {
        const string Endpoint = "https://api.met.no/weatherapi/locationforecast/2.0/compact?lat={0}&lon={1}";

        [SerializeField] WeatherCycle weather;
        [SerializeField] float latitude = -1.94f;     // Kimironko (terrain centre of the story district)
        [SerializeField] float longitude = 30.132f;
        [SerializeField] float refreshMinutes = 30f;
        [SerializeField] float timeoutSeconds = 5f;
        [SerializeField, Tooltip("MET Norway asks for an app name plus a contact (website or email). Fill in before use.")]
        string userAgent = "KigaliRush-StudentProject/0.1 (contact: TODO)";
        [SerializeField] bool online = false;
        [SerializeField, Tooltip("One weather per session, changing each launch (sunny first). Off = live forecast.")]
        bool perSession = true;

        int _session = 1;

        public WeatherNow Current { get; private set; }
        public string Summary => Describe(Current);

        public void Configure(WeatherCycle cycle) => weather = cycle;

        // Called by StoryDirector before Start with the session count from the save (1 = first ever session).
        public void BeginSession(int session) => _session = Mathf.Max(1, session);

        void Start()
        {
            if (perSession)
            {
                Current = ForSession(_session);
                Apply(Current);
                if (weather != null)
                    weather.SnapToForced();
                return;
            }
            Current = Fallback();
            Apply(Current);
            StartCoroutine(Loop());
        }

        // The session weathers, in order. Sunny first; consecutive sessions never repeat.
        static WeatherNow ForSession(int session)
        {
            var now = KigaliTime.Now;
            float h = (float)now.TimeOfDay.TotalHours;
            float temp = Mathf.Lerp(17f, 27f, Mathf.Clamp01(Mathf.Sin((h - 6f) / 12f * Mathf.PI)));
            var w = new WeatherNow { Live = false, FetchedKigali = now };
            switch ((session - 1) % 5)
            {
                case 0: w.CloudPercent = 5f; w.WindMs = 1.5f; w.Symbol = "clearsky_day"; w.TemperatureC = temp + 1f; break;
                case 1: w.CloudPercent = 45f; w.WindMs = 3.5f; w.Symbol = "partlycloudy_day"; w.TemperatureC = temp; break;
                case 2: w.CloudPercent = 92f; w.WindMs = 2.5f; w.Symbol = "cloudy"; w.TemperatureC = temp - 2f; break;
                case 3: w.CloudPercent = 95f; w.WindMs = 5f; w.RainMmNextHour = 1.5f; w.Symbol = "rainshowers_day"; w.TemperatureC = temp - 4f; break;
                default: w.CloudPercent = 20f; w.WindMs = 8f; w.Symbol = "fair_day"; w.TemperatureC = temp; break;
            }
            return w;
        }

        IEnumerator Loop()
        {
            while (true)
            {
                if (online)
                    yield return Fetch();
                yield return new WaitForSecondsRealtime(refreshMinutes * 60f);
            }
        }

        IEnumerator Fetch()
        {
            string url = string.Format(System.Globalization.CultureInfo.InvariantCulture, Endpoint, latitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                                       longitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            using var req = UnityWebRequest.Get(url);
            req.timeout = Mathf.CeilToInt(timeoutSeconds);
            req.SetRequestHeader("User-Agent", userAgent);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                yield break;   // keep the fallback; no error reaches the player
            try
            {
                var doc = JsonUtility.FromJson<MetDoc>(req.downloadHandler.text);
                var ts = doc?.properties?.timeseries;
                if (ts == null || ts.Length == 0)
                    yield break;
                var d = ts[0].data;
                Current = new WeatherNow
                {
                    Live = true,
                    TemperatureC = d.instant.details.air_temperature,
                    WindMs = d.instant.details.wind_speed,
                    CloudPercent = d.instant.details.cloud_area_fraction,
                    RainMmNextHour = d.next_1_hours?.details?.precipitation_amount ?? 0f,
                    Symbol = d.next_1_hours?.summary?.symbol_code ?? "",
                    FetchedKigali = KigaliTime.Now,
                };
                Apply(Current);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KigaliWeather] Could not read forecast: {e.Message}");
            }
        }

        // Offline pattern: the two rainy seasons (roughly March-May and September-December) bring afternoon
        // showers; mornings are calm. A gameplay approximation, not climatology data (TODO: Meteo Rwanda normals).
        static WeatherNow Fallback()
        {
            var now = KigaliTime.Now;
            bool wet = now.Month >= 3 && now.Month <= 5 || now.Month >= 9;
            float h = (float)now.TimeOfDay.TotalHours;
            bool shower = wet && h >= 14f && h < 17.5f;
            return new WeatherNow
            {
                Live = false,
                TemperatureC = Mathf.Lerp(17f, 26f, Mathf.Clamp01(Mathf.Sin((h - 6f) / 12f * Mathf.PI))),
                WindMs = h > 11f && h < 17f ? 4f : 1.5f,
                CloudPercent = shower ? 85f : wet ? 55f : 30f,
                RainMmNextHour = shower ? 1.5f : 0f,
                Symbol = shower ? "rainshowers_day" : "partlycloudy_day",
                FetchedKigali = now,
            };
        }

        void Apply(WeatherNow w)
        {
            if (weather == null)
                return;
            WeatherCycle.Kind kind = w.RainMmNextHour > 0.15f || (w.Symbol?.Contains("rain") ?? false) ? WeatherCycle.Kind.Shower
                : w.CloudPercent > 75f ? WeatherCycle.Kind.Overcast
                : w.WindMs > 7f ? WeatherCycle.Kind.Windy
                : w.WindMs > 3f ? WeatherCycle.Kind.Breeze
                : WeatherCycle.Kind.Calm;
            weather.Force(kind);
        }

        public static string Describe(WeatherNow w)
        {
            string sky = w.RainMmNextHour > 2f ? "heavy rain" : w.RainMmNextHour > 0.15f ? "rain showers"
                : w.CloudPercent > 75f ? "overcast" : w.CloudPercent > 35f ? "partly cloudy"
                : KigaliTime.HourOfDay >= 6f && KigaliTime.HourOfDay < 18.5f ? "sunny" : "clear";
            return $"{w.TemperatureC:0}°C, {sky}, wind {w.WindMs * 3.6f:0} km/h{(w.Live ? " (live)" : "")}";
        }

        // Minimal JSON shape of the compact forecast (field names must match the API).
        [Serializable] class MetDoc { public Props properties; }
        [Serializable] class Props { public Step[] timeseries; }
        [Serializable] class Step { public string time; public StepData data; }
        [Serializable] class StepData { public Instant instant; public Next next_1_hours; }
        [Serializable] class Instant { public InstantDetails details; }
        [Serializable] class InstantDetails { public float air_temperature; public float wind_speed; public float cloud_area_fraction; }
        [Serializable] class Next { public SymbolInfo summary; public NextDetails details; }
        [Serializable] class SymbolInfo { public string symbol_code; }
        [Serializable] class NextDetails { public float precipitation_amount; }
    }
}
