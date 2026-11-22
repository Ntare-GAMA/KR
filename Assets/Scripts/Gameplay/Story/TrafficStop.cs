using UnityEngine;

namespace KR.Gameplay.Story
{
    // An officer pulling you over (Law 014/2026): pull over within 15 s and stay put for the 10 s check (Art. 6:
    // officers' instructions prevail). Moving again restarts part of the check. Driving more than 120 m away is
    // refusing to stop (Art. 39). Plain C#, no scene objects, so it can be unit-tested.
    public sealed class TrafficStop
    {
        public enum Outcome { Pending, Cleared, Refused }

        const float PullOverSeconds = 15f, CheckSeconds = 10f, RestartAtLeast = 6f, RefuseMetres = 120f, StoppedKmh = 2f;

        Vector3 _at;
        float _timer;
        bool _holding;

        public RoadRule Rule { get; private set; }
        public string Message { get; private set; }
        public bool Active { get; private set; }

        public void Start(RoadRule rule, Vector3 where)
        {
            Rule = rule;
            _at = where;
            _timer = PullOverSeconds;
            _holding = false;
            Active = true;
            Message = "An officer signals you to stop. Pull over now.";
        }

        public void Cancel()
        {
            Active = false;
            Message = null;
        }

        public Outcome Update(float dt, Vector3 player, float speedKmh)
        {
            if (!Active)
                return Outcome.Pending;
            if (Vector2.Distance(new Vector2(player.x, player.z), new Vector2(_at.x, _at.z)) > RefuseMetres)
            {
                Cancel();
                return Outcome.Refused;
            }
            bool stopped = speedKmh < StoppedKmh;
            _timer -= dt;
            if (!_holding)
            {
                if (stopped)
                {
                    _holding = true;
                    _timer = CheckSeconds;
                    Message = "The officer checks your licence and insurance.";
                }
                else if (_timer <= 0f)
                {
                    Message = "You did not stop. The officer is following you.";
                    _timer = PullOverSeconds;
                }
                return Outcome.Pending;
            }
            if (!stopped)
                _timer = Mathf.Max(_timer, RestartAtLeast);
            if (_timer > 0f)
                return Outcome.Pending;
            Cancel();
            return Outcome.Cleared;
        }
    }
}
