using KR.Gameplay.Player;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Story Mode HUD (desktop IMGUI prototype; VR needs a world-space panel instead, since IMGUI does not draw in the headset):
    // Kigali clock and weather, the milestone and its rush clock, rule toasts, police stops, results with the
    // knowledge card, and a news ticker that mixes live time and weather with authored local news.
    public sealed class StoryHud : MonoBehaviour
    {
        [SerializeField] StoryDirector director;

        PlayerRig _rig;
        Navigator _nav;
        GUIStyle _big, _body, _small, _clock;
        Texture2D _panel;
        int _newsIndex;
        float _newsTimer;

        public void Configure(StoryDirector story) => director = story;

        void Styles()
        {
            if (_big != null)
                return;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _clock = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _panel = new Texture2D(1, 1);
            _panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
            _panel.Apply();
        }

        void Panel(Rect r) => GUI.DrawTexture(r, _panel);

        public static string LicenceText(StoryDirector d) => d.Learner
            ? $"Learner (no fines): {d.CleanMetres / 1000f:0.0}/{d.LicenceMetres / 1000f:0.#} km clean"
            : "Full licence: fines apply";

        void OnGUI()
        {
            if (director == null || director.Week == null || VrRig.Active)
                return;      // the headset shows VrHud instead
            if (_rig == null)
                _rig = FindFirstObjectByType<PlayerRig>();
            if (_rig != null && _rig.CurrentMode == PlayerRig.Mode.Driving)
                return;      // in the car everything is on the dashboard screen
            Styles();
            float w = Screen.width, h = Screen.height;
            var m = director.Current;


            // Top left: Kigali time, weather, traffic.
            Panel(new Rect(12, 12, 360, 92));
            GUI.Label(new Rect(22, 16, 340, 30), $"Kigali {KigaliTime.Clock}   {director.DayName}", _body);
            string sky = director.Weather != null ? director.Weather.Summary : "";
            GUI.Label(new Rect(22, 42, 340, 22), sky, _small);
            string jam = director.Rhythm != null && director.Rhythm.Jammed ? "Heavy traffic: rush hour" : "Traffic moving";
            string belt = director.Rules != null && director.Rules.Mode == Travel.Car
                ? $"   {director.Rules.SpeedKmh:0}/{director.Rules.LimitKmh:0} km/h   gear {(director.Rules.Driven != null ? director.Rules.Driven.Gear : "-")}   belt {(director.Rules.SeatbeltOn ? "on" : "OFF")} (B)" : "";
            GUI.Label(new Rect(22, 64, 340, 36), jam + belt, _small);

            // Top right: record.
            Panel(new Rect(w - 252, 12, 240, 92));
            GUI.Label(new Rect(w - 242, 16, 230, 22), $"Knowledge cards: {director.CardCount}", _small);
            GUI.Label(new Rect(w - 242, 36, 230, 22), $"Demerit points: {director.Demerits}", _small);
            GUI.Label(new Rect(w - 242, 56, 230, 22), $"Fines: FRW {director.Fines:N0}", _small);
            GUI.Label(new Rect(w - 242, 76, 230, 22), LicenceText(director), _small);

            // Satnav directions, under the clock.
            if (_nav == null)
                _nav = FindFirstObjectByType<Navigator>();
            if (_nav != null && _nav.Instruction != null)
            {
                Panel(new Rect(w / 2 - 300, 116, 600, 34));
                GUI.Label(new Rect(w / 2 - 290, 120, 580, 28), "Satnav: " + _nav.Instruction + "   (M map, N destination)", _small);
            }

            // Driving coach: what to do next, before a rule is broken.
            var coach = director.Coach;
            if (coach != null && coach.Line != null)
            {
                var keep = GUI.color;
                GUI.color = coach.Level == DrivingCoach.Tone.Warning ? new Color(1f, 0.45f, 0.4f)
                    : coach.Level == DrivingCoach.Tone.Caution ? new Color(1f, 0.85f, 0.4f) : new Color(0.75f, 1f, 0.8f);
                Panel(new Rect(w / 2 - 360, h - 300, 720, 48));
                GUI.Label(new Rect(w / 2 - 350, h - 296, 700, 42), "Coach: " + coach.Line, _body);
                GUI.color = keep;
            }

            switch (director.State)
            {
                case StoryDirector.Phase.Briefing when m != null && !Wallet.Showing && !ArrestScreen.Showing:   // never over the cards (owner 2026-11-23)
                    Panel(new Rect(w / 2 - 330, h / 2 - 150, 660, 300));
                    GUI.Label(new Rect(w / 2 - 310, h / 2 - 140, 620, 40), $"{director.DayName}: {m.title}", _big);
                    GUI.Label(new Rect(w / 2 - 310, h / 2 - 92, 620, 120), m.brief, _body);
                    var best = director.BestFor(m.id);
                    string bestText = best != null && best.bestSeconds >= 0f ? $"   Your best: {StoryDirector.Mmss(best.bestSeconds)} ({best.bestStars}★)" : "";
                    GUI.Label(new Rect(w / 2 - 310, h / 2 + 40, 620, 30), $"Go to: {m.placeName}   Par: {StoryDirector.Mmss(m.parSeconds)}{bestText}", _body);
                    GUI.Label(new Rect(w / 2 - 310, h / 2 + 90, 620, 40), "Press Enter to start the clock. Follow the road rules: breaking one always costs more time than it saves.", _small);
                    break;

                case StoryDirector.Phase.Rushing:
                case StoryDirector.Phase.Dwelling:
                case StoryDirector.Phase.PoliceStop:
                    float left = director.Remaining;
                    var old = GUI.color;
                    GUI.color = left < 0f ? new Color(1f, 0.35f, 0.3f) : left < 30f ? new Color(1f, 0.8f, 0.3f) : Color.white;
                    Panel(new Rect(w / 2 - 170, 12, 340, 96));
                    GUI.Label(new Rect(w / 2 - 170, 14, 340, 54), StoryDirector.Mmss(left), _clock);
                    GUI.color = old;
                    GUI.Label(new Rect(w / 2 - 160, 66, 320, 40),
                              $"{m?.title}  ·  {m?.placeName}  ·  {director.Distance:0} m{(director.Penalty > 0f ? $"  ·  +{director.Penalty:0} s" : "")}", _small);
                    if (director.State == StoryDirector.Phase.Dwelling && m != null)
                    {
                        Panel(new Rect(w / 2 - 200, h - 170, 400, 54));
                        GUI.Label(new Rect(w / 2 - 190, h - 166, 380, 24), m.dwellLabel, _body);
                        GUI.DrawTexture(new Rect(w / 2 - 190, h - 138, 380 * director.DwellProgress, 14), Texture2D.whiteTexture);
                    }
                    if (director.State == StoryDirector.Phase.PoliceStop && director.StopMessage != null)
                    {
                        Panel(new Rect(w / 2 - 300, h / 2 - 50, 600, 100));
                        GUI.Label(new Rect(w / 2 - 285, h / 2 - 40, 570, 80), "POLICE\n" + director.StopMessage, _body);
                    }
                    break;

                case StoryDirector.Phase.Results:
                    Panel(new Rect(w / 2 - 330, h / 2 - 170, 660, 340));
                    string stars = new string('★', director.LastStars) + new string('☆', 3 - director.LastStars);
                    GUI.Label(new Rect(w / 2 - 310, h / 2 - 160, 620, 40), $"Done in {StoryDirector.Mmss(director.LastTime)}  {stars}{(director.LastWasBest ? "  New best!" : "")}", _big);
                    var card = director.LastCard;
                    if (card != null)
                    {
                        GUI.Label(new Rect(w / 2 - 310, h / 2 - 110, 620, 28), $"Knowledge card: {card.title}", _body);
                        GUI.Label(new Rect(w / 2 - 310, h / 2 - 80, 620, 120), card.textEn + (string.IsNullOrEmpty(card.textRw) ? "" : $"\n{card.textRw}"), _small);
                        string src = card.sources != null && card.sources.Length > 0 ? string.Join("; ", card.sources) : "no source yet";
                        GUI.Label(new Rect(w / 2 - 310, h / 2 + 50, 620, 50), $"Source: {src}   [{card.reviewStatus}]", _small);
                    }
                    GUI.Label(new Rect(w / 2 - 310, h / 2 + 120, 620, 30), "Press Enter for the next milestone.", _small);
                    break;

                case StoryDirector.Phase.WeekDone:
                    Panel(new Rect(w / 2 - 300, h / 2 - 60, 600, 120));
                    GUI.Label(new Rect(w / 2 - 285, h / 2 - 50, 570, 100), "Rush Week complete. More days are coming as the content grows.", _big);
                    break;
            }

            // Player prompt ("E  Get in the car") and the player's own messages.
            if (_rig == null)
                _rig = FindFirstObjectByType<PlayerRig>();
            if (_rig != null)
            {
                string hint = _rig.CurrentHint;
                if (hint != null)
                {
                    Panel(new Rect(w / 2 - 260, h - 230, 520, 40));
                    GUI.Label(new Rect(w / 2 - 250, h - 226, 500, 32), hint, _body);
                }
                if (_rig.Prompt != null)
                {
                    Panel(new Rect(w / 2 - 160, h - 180, 320, 40));
                    GUI.Label(new Rect(w / 2 - 150, h - 176, 300, 32), _rig.Prompt, _body);
                }
                for (int t = 0; t < _rig.Toasts.Count; t++)
                {
                    Panel(new Rect(16, h - 230 + t * 44, 520, 40));
                    GUI.Label(new Rect(26, h - 226 + t * 44, 500, 32), _rig.Toasts[t].text, _small);
                }
            }

            if (director.Toast != null && Time.time < director.ToastUntil)
            {
                Panel(new Rect(w / 2 - 360, h - 110, 720, 60));
                GUI.Label(new Rect(w / 2 - 350, h - 106, 700, 56), director.Toast, _body);
            }

            // News ticker: live time and weather first, then authored local news (generic, non-political).
            var news = director.Week.news;
            _newsTimer -= Time.unscaledDeltaTime;
            if (_newsTimer <= 0f)
            {
                _newsTimer = 9f;
                _newsIndex++;
            }
            int count = (news?.Length ?? 0) + 1;
            int i = _newsIndex % count;
            string line = i == 0 ? $"KIGALI {KigaliTime.Clock}  ·  {(director.Weather != null ? director.Weather.Summary : "")}" : news[i - 1];
            Panel(new Rect(0, h - 34, w, 34));
            GUI.Label(new Rect(14, h - 31, w - 28, 28), "NEWS  " + line, _small);
        }
    }
}
