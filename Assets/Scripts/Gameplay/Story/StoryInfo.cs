using System.Text;
using KR.Gameplay.Player;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The Story Mode information as rich text, for the car's dashboard screen and the walking info panel.
    // In VR every instruction names a controller button.
    public static class StoryInfo
    {
        static readonly StringBuilder Sb = new StringBuilder();

        public static string Compose(StoryDirector d, PlayerRig rig, Navigator nav, bool inCar)
        {
            var sb = Sb;
            sb.Clear();
            if (d == null || d.Week == null)
                return "";
            bool vr = VrRig.Active;
            var m = d.Current;
            var rules = d.Rules;
            var car = rules != null ? rules.Driven : null;

            // Clock or the milestone countdown, large.
            bool racing = d.State == StoryDirector.Phase.Rushing || d.State == StoryDirector.Phase.Dwelling || d.State == StoryDirector.Phase.PoliceStop;
            if (racing && m != null)
            {
                float left = d.Remaining;
                string colour = left < 0f ? "#ff6655" : left < 30f ? "#ffcc55" : "#ffffff";
                sb.Append($"<b><size=52><color={colour}>{StoryDirector.Mmss(left)}</color></size></b>   {m.title}\n");
                sb.Append($"{m.placeName}  ·  {d.Distance:0} m{(d.Penalty > 0f ? $"  ·  +{d.Penalty:0} s" : "")}\n");
            }
            else
                sb.Append($"<b><size=40>{KigaliTime.Clock}</size></b>   {d.DayName}   {(d.Weather != null ? d.Weather.Summary : "")}\n");

            if (inCar && rules != null)
            {
                string speedColour = rules.SpeedKmh > rules.LimitKmh + 1f ? "#ff6655" : "#ffffff";
                sb.Append($"<b><size=36><color={speedColour}>{rules.SpeedKmh:0}</color></size></b> km/h  (limit {rules.LimitKmh:0})   ");
                sb.Append($"Gear <b>{(car != null ? car.Gear : "-")}</b>   Belt {(rules.SeatbeltOn ? "on" : "<color=#ff6655><b>OFF</b></color>")}   ");
                var lights = car != null ? car.GetComponent<CarLights>() : null;
                sb.Append($"Lights {(lights != null && lights.HeadlightsOn ? "on" : "off")}\n");
            }

            if (nav != null && nav.Instruction != null)
                sb.Append($"<color=#66ddff><b>Satnav:</b> {nav.Instruction}</color>\n");
            var coach = d.Coach;
            if (coach != null && coach.Line != null)
            {
                string c = coach.Level == DrivingCoach.Tone.Warning ? "#ff7766" : coach.Level == DrivingCoach.Tone.Caution ? "#ffd966" : "#bfffcc";
                sb.Append($"<color={c}><b>Coach:</b> {coach.Line}</color>\n");
            }
            if (d.State == StoryDirector.Phase.PoliceStop && d.StopMessage != null)
                sb.Append($"<color=#ff7766><b>POLICE:</b> {d.StopMessage}</color>\n");
            if (d.State == StoryDirector.Phase.Dwelling && m != null)
                sb.Append($"{m.dwellLabel}  {d.DwellProgress * 100f:0}%\n");

            string confirm = vr ? "X" : "Enter";
            switch (d.State)
            {
                case StoryDirector.Phase.Briefing when m != null:
                    sb.Append($"\n<b>{m.title}</b>  ·  go to {m.placeName}  ·  par {StoryDirector.Mmss(m.parSeconds)}\n");
                    if (!inCar)
                        sb.Append($"{m.brief}\n");
                    sb.Append($"<b>Press {confirm} to start the clock.</b>\n");
                    break;
                case StoryDirector.Phase.Results:
                    sb.Append($"\n<b>Done in {StoryDirector.Mmss(d.LastTime)}  {new string('★', d.LastStars)}{new string('☆', 3 - d.LastStars)}</b>\n");
                    if (d.LastCard != null)
                        sb.Append(inCar ? $"Knowledge card: {d.LastCard.title}\n" : $"Knowledge card: <b>{d.LastCard.title}</b>\n{d.LastCard.textEn}\n");
                    sb.Append($"Press {confirm} for the next milestone.\n");
                    break;
                case StoryDirector.Phase.WeekDone:
                    sb.Append("\n<b>Rush Week complete.</b>\n");
                    break;
            }

            if (!inCar)
                sb.Append($"\nCards {d.CardCount}   Demerits {d.Demerits}   Fines FRW {d.Fines:N0}   {StoryHud.LicenceText(d)}\n");
            if (rig != null && rig.Prompt != null)
                sb.Append($"<b>{rig.Prompt}</b>\n");
            if (d.Toast != null && Time.time < d.ToastUntil)
                sb.Append($"{d.Toast}\n");
            if (rig != null)
                foreach (var (text, _) in rig.Toasts)
                    sb.Append($"{text}\n");
            return sb.ToString();
        }
    }
}
