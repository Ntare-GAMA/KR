using UnityEngine;

namespace KR.Gameplay.Player
{
    // The player's car (PolyDrive Bavaria Sport) on real physics: a Rigidbody on four WheelColliders with suspension,
    // tyre grip, engine torque, brakes and anti-roll bars, so it rides on the actual road surface (camber, slopes,
    // kerbs) and collides with the city. Rear-wheel drive with an automatic gearbox:
    //   accelerator (W / right trigger) drives in the selected gear, D or R (R key / right stick click, only when
    //   nearly stopped); the brake (S / left trigger) only ever brakes, with anti-lock so the wheels keep turning;
    //   in D with no pedal pressed the car creeps forward like a real automatic; Space / B is the handbrake.
    // The cabin is animated from the inputs: the steering wheel turns, the pedals press, and the driver's hands,
    // right foot and hips have targets here (the grips are children of the steering wheel, so hands turn with it).
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DrivableCar : MonoBehaviour
    {
        [SerializeField] Transform seat;
        [SerializeField] Transform[] wheels;              // visual pivots: FL, FR, RL, RR
        [SerializeField] WheelCollider[] colliders;       // same order
        [SerializeField] Transform steeringWheel;         // pivot; its local Z is the steering column axis
        [SerializeField] Transform leftGrip, rightGrip;   // children of the steering wheel (9 and 3 o'clock)
        [SerializeField] Transform accelerator, brake;    // pedal pivots (hinge on local X)
        [SerializeField] Transform acceleratorFoot, brakeFoot, restFoot;
        [SerializeField] Transform hipPoint;              // where the driver's hips rest on the seat
        [SerializeField] Vector2 halfSize = new Vector2(0.9f, 2.2f);

        [Header("Engine and brakes")]
        [SerializeField] float maxSpeed = 33f;            // m/s (about 120 km/h); the road rules ask for far less
        [SerializeField] float reverseSpeed = 5f;
        [SerializeField] float wheelTorque = 1500f;       // total at the rear wheels from standstill (N m): ~0-50 km/h in 5 s
        [SerializeField] float creepSpeed = 1.6f;         // automatic creep in D with no pedal (m/s, about 6 km/h)
        [SerializeField] float brakeTorque = 1100f;       // per wheel at full pedal: about 0.8 g, no locking
        [SerializeField] float handbrakeTorque = 3000f;   // rear wheels
        [SerializeField] float engineBraking = 60f;
        [Header("Steering")]
        [SerializeField] float maxSteer = 34f;            // road-wheel angle at walking pace
        [SerializeField] float highSpeedSteer = 9f;       // road-wheel angle at maxSpeed
        [SerializeField] float steerRate = 2.2f;          // full lock per second
        [SerializeField] float steeringRatio = 14f;       // steering-wheel degrees per road-wheel degree
        [Header("Stability")]
        [SerializeField] float antiRoll = 6500f;
        [SerializeField] float downforce = 1.2f;
        [Header("Slopes and recovery")]
        [SerializeField, Range(0f, 1f), Tooltip("Share of the downhill pull cancelled while driving uphill (hill-start assist).")]
        float hillAssist = 0.85f;
        [SerializeField, Tooltip("Extra push from the accelerator below this speed (m/s), to drive out of ditches and off humps.")]
        float crawlAssistBelow = 2.5f;
        [SerializeField, Tooltip("Strength of that push at a standstill (m/s²).")] float crawlAssist = 3f;

        Rigidbody _rb;
        float _steer, _throttle, _brake;
        bool _handbrake, _reverse, _park = true, _parkHint;
        Quaternion _wheelBase, _accBase, _brakeBase;

        public bool Driven { get; private set; }
        public float Speed { get; private set; }          // signed, along the car's forward axis (m/s)
        public Transform Seat => seat;
        public Transform LeftGrip => leftGrip;
        public Transform RightGrip => rightGrip;
        public Transform HipPoint => hipPoint;
        public float Throttle => _throttle;
        public float BrakePedal => _brake;
        // Road-wheel angle (degrees, + to the right).
        public float SteerAngle => _steer;
        public bool Reverse => _reverse;
        // P (park) holds the car and is the only gear you can get out in; the right stick click cycles P, D, R.
        public string Gear => !Driven || _park ? "P" : _reverse ? "R" : "D";
        public bool Parked => !Driven || _park;
        public event System.Action<string> Message;
        // Where the driver's eyes are: above the hip point and a little forward, low enough that the whole
        // windscreen is in view rather than the headlining.
        public Vector3 EyePoint => hipPoint != null ? hipPoint.position + transform.up * 0.62f + transform.forward * 0.12f
                                                     : seat.position + transform.up * 0.62f;
        // The right foot rests on whichever pedal is in use.
        public Transform RightFootTarget => _brake > 0.05f ? brakeFoot : Mathf.Abs(_throttle) > 0.05f ? acceleratorFoot : restFoot;

        public void Configure(Transform driverSeat, Transform[] wheelPivots, WheelCollider[] wheelColliders, Transform steering,
                              Transform gripLeft, Transform gripRight, Transform pedalAccelerator, Transform pedalBrake,
                              Transform footAccelerator, Transform footBrake, Transform footRest, Transform hips, Vector2 half)
        {
            seat = driverSeat;
            wheels = wheelPivots;
            colliders = wheelColliders;
            steeringWheel = steering;
            leftGrip = gripLeft;
            rightGrip = gripRight;
            accelerator = pedalAccelerator;
            brake = pedalBrake;
            acceleratorFoot = footAccelerator;
            brakeFoot = footBrake;
            restFoot = footRest;
            hipPoint = hips;
            halfSize = half;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;   // smooth in the headset between physics steps
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (steeringWheel != null) _wheelBase = steeringWheel.localRotation;
            if (accelerator != null) _accBase = accelerator.localRotation;
            if (brake != null) _brakeBase = brake.localRotation;
            if (colliders != null && colliders.Length > 0 && colliders[0] != null)
                colliders[0].ConfigureVehicleSubsteps(5f, 12, 15);    // steadier at walking pace
        }

        public void SetDriven(bool on)
        {
            Driven = on;
            if (!on)
            {
                _throttle = _brake = 0f;
                _reverse = false;
                _park = true;
            }
        }

        // Driver's door: Rwanda drives on the right, so the driver sits on the left.
        public Vector3 ExitPoint
        {
            get
            {
                var p = transform.position - transform.right * (halfSize.x + 0.9f);
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore))
                    p.y = hit.point.y;
                return p;
            }
        }

        void Update()
        {
            _handbrake = !Driven || _park || RigInput.Handbrake;
            Speed = Vector3.Dot(_rb.linearVelocity, transform.forward);
            float accel = Driven ? RigInput.Accelerator : 0f;
            float brake = Driven ? RigInput.Brake : 0f;
            float steerIn = Driven ? RigInput.Steer : 0f;

            if (Driven && RigInput.GearPressed)
            {
                if (Mathf.Abs(Speed) < 0.8f)
                {
                    // P -> D -> R -> P
                    if (_park) { _park = false; _reverse = false; }
                    else if (!_reverse) _reverse = true;
                    else { _reverse = false; _park = true; }
                    Message?.Invoke(_park ? "Gear P: parked. You can get out now." : _reverse
                        ? "Gear R: reverse. The accelerator now drives backwards." : "Gear D: drive.");
                }
                else
                    Message?.Invoke("Stop first, then change gear.");
            }

            if (_park && accel > 0.3f && !_parkHint)
            {
                _parkHint = true;
                Message?.Invoke(VrRig.Active ? "In P. Click the right stick for D to drive." : "In P. Press R for D to drive.");
            }
            if (accel < 0.1f)
                _parkHint = false;
            if (_park)
                accel = 0f;
            _throttle = _reverse ? -accel : accel;
            _brake = brake;

            float lockAngle = Mathf.Lerp(maxSteer, highSpeedSteer, Mathf.Clamp01(Mathf.Abs(Speed) / maxSpeed));
            _steer = Mathf.MoveTowards(_steer, steerIn * lockAngle, steerRate * maxSteer * Time.deltaTime);
            AnimateCabin();
        }

        void FixedUpdate()
        {
            if (colliders == null || colliders.Length < 4)
                return;
            float along = _reverse ? -Speed : Speed;   // speed in the direction of the selected gear
            float top = _reverse ? reverseSpeed : maxSpeed;
            float pedal = Mathf.Abs(_throttle);
            // Torque falls away towards top speed; with no pedal in gear the car creeps (idle torque).
            float drive = pedal * wheelTorque * Mathf.Clamp01(1f - along / top);
            if (Driven && pedal < 0.02f && _brake < 0.02f && !_handbrake && along < creepSpeed)
                drive = wheelTorque * 0.12f * Mathf.Clamp01(1f - along / creepSpeed);
            float motor = (_reverse ? -drive : drive) * 0.5f;   // per rear wheel

            for (int i = 0; i < 4; i++)
            {
                var w = colliders[i];
                if (w == null)
                    continue;
                bool front = i < 2;
                if (front)
                    w.steerAngle = _steer;
                w.motorTorque = front ? 0f : motor;
                float b = _brake * brakeTorque * (front ? 1.15f : 0.85f);   // more at the front, as on a real car
                // Anti-lock: if this wheel turns much slower than the car is moving, ease the brake off.
                if (b > 0f && Mathf.Abs(Speed) > 2f)
                {
                    float wheelSpeed = Mathf.Abs(w.rpm) * w.radius * 2f * Mathf.PI / 60f;
                    if (wheelSpeed < Mathf.Abs(Speed) * 0.75f)
                        b *= 0.35f;
                }
                if (pedal < 0.02f && _brake < 0.02f)
                    b = Mathf.Max(b, Mathf.Abs(along) > creepSpeed ? engineBraking : 0f);
                // Accelerating against the direction of travel (just changed gear): brake to a stop first.
                if (pedal > 0.02f && along < -0.5f)
                    b = Mathf.Max(b, brakeTorque * 0.6f);
                if (!front && _handbrake)
                    b = Mathf.Max(b, handbrakeTorque);
                w.brakeTorque = b;
            }
            AntiRoll(colliders[0], colliders[1]);
            AntiRoll(colliders[2], colliders[3]);
            _rb.AddForce(-transform.up * downforce * Speed * Speed, ForceMode.Force);
            Slopes(pedal, along);
            Recover();
        }

        // Hills, like a modern automatic: with a pedal pressed most of the downhill pull is cancelled so the car
        // pulls away on any slope it can grip; stopped in gear with no pedal, hill-hold keeps it from rolling back.
        void Slopes(float pedal, float along)
        {
            int grounded = 0;
            foreach (var w in colliders)
                if (w != null && w.isGrounded)
                    grounded++;
            if (!Driven || grounded < 2)
                return;
            Vector3 gearDir = _reverse ? -transform.forward : transform.forward;
            float pull = Vector3.Dot(Physics.gravity, gearDir) * _rb.mass;   // < 0 when the way ahead is uphill
            if (pedal > 0.02f && pull < 0f)
                _rb.AddForce(-gearDir * pull * hillAssist, ForceMode.Force);
            else if (pedal < 0.02f && Mathf.Abs(along) < 0.6f)
            {
                // Hill-hold: cancel the slope, so stopped on a hill the car behaves as on the flat (creeps in D).
                Vector3 slope = Vector3.ProjectOnPlane(Physics.gravity, transform.up) * _rb.mass;
                _rb.AddForce(-slope, ForceMode.Force);
            }
        }

        // Drive out of anywhere, under your own control: at crawling speed the accelerator also pushes the car
        // along (enough to climb out of a ditch, off steep grass or off a hump it is resting on, even with wheels
        // in the air) and the steering turns it, so it never needs moving for you. Only a car on its roof or side
        // is set back on its wheels, where it is.
        float _upsideDown;
        void Recover()
        {
            if (!Driven)
            {
                _upsideDown = 0f;
                return;
            }
            float pedal = Mathf.Abs(_throttle);
            float crawl = Mathf.Abs(Speed);
            if (pedal > 0.05f && crawl < crawlAssistBelow)
            {
                Vector3 gearDir = _reverse ? -transform.forward : transform.forward;
                gearDir = Vector3.ProjectOnPlane(gearDir, Vector3.up).normalized;
                float k = pedal * (1f - crawl / crawlAssistBelow);
                _rb.AddForce(gearDir * crawlAssist * k, ForceMode.Acceleration);
                float steer = maxSteer > 0f ? _steer / maxSteer : 0f;
                _rb.AddTorque(Vector3.up * steer * (_reverse ? -1f : 1f) * 1.2f * k, ForceMode.Acceleration);
            }

            bool tipped = Vector3.Dot(transform.up, Vector3.up) < 0.3f;
            _upsideDown = tipped ? _upsideDown + Time.fixedDeltaTime : 0f;
            if (_upsideDown < 2f)
                return;
            _upsideDown = 0f;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f)
                fwd = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position += Vector3.up * 1.2f;
            _rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            transform.SetPositionAndRotation(_rb.position, _rb.rotation);
            Message?.Invoke("The car was turned back onto its wheels.");
        }

        // Transfers load between the wheels of one axle so the body does not lean over in corners.
        void AntiRoll(WheelCollider left, WheelCollider right)
        {
            if (left == null || right == null)
                return;
            float travelL = 1f, travelR = 1f;
            bool groundedL = left.GetGroundHit(out var hitL);
            bool groundedR = right.GetGroundHit(out var hitR);
            if (groundedL)
                travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance;
            if (groundedR)
                travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance;
            float force = (travelL - travelR) * antiRoll;
            if (groundedL)
                _rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedR)
                _rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        void LateUpdate()
        {
            // Visual wheels follow the physics wheels (suspension travel, spin, steering).
            if (wheels == null || colliders == null)
                return;
            for (int i = 0; i < wheels.Length && i < colliders.Length; i++)
                if (wheels[i] != null && colliders[i] != null)
                {
                    colliders[i].GetWorldPose(out var p, out var r);
                    wheels[i].SetPositionAndRotation(p, r);
                }
        }

        void AnimateCabin()
        {
            if (steeringWheel != null)
                steeringWheel.localRotation = _wheelBase * Quaternion.AngleAxis(-_steer * steeringRatio, Vector3.forward);
            if (accelerator != null)
                accelerator.localRotation = _accBase * Quaternion.AngleAxis(-Mathf.Abs(_throttle) * 18f, Vector3.right);
            if (brake != null)
                brake.localRotation = _brakeBase * Quaternion.AngleAxis(-_brake * 18f, Vector3.right);
        }
    }
}
