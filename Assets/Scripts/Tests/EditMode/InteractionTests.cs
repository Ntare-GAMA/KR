using KR.Gameplay.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace KR.Tests
{
    // The hands are written against IHandDriver, so a scripted driver stands in for a Quest controller here.
    public class InteractionTests
    {
        sealed class ScriptedDriver : IHandDriver
        {
            public bool Closed { get; set; }
            public bool HoldsInFront { get; set; }
            public void UpdatePose(Transform hand) { }
            public IGrabbable FindTarget(Transform hand) => null;
            public Vector3? ThrowVelocity(Transform hand) => null;
            public void Haptic(float amplitude, float seconds) { }
        }

        GameObject _root;

        [SetUp] public void SetUp() => _root = new GameObject("test root");
        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

        HandInteractor Hand(Vector3 at)
        {
            var hand = HandInteractor.Create(_root.transform, "hand", false, new ScriptedDriver());
            hand.transform.position = at;
            return hand;
        }

        DoorHandle Door(out Transform hinge)
        {
            var frame = new GameObject("door");
            frame.transform.SetParent(_root.transform, false);
            hinge = new GameObject("hinge").transform;
            hinge.SetParent(frame.transform, false);
            var handleGo = new GameObject("handle");
            handleGo.transform.SetParent(hinge, false);
            handleGo.transform.localPosition = new Vector3(0.9f, 1f, 0f);
            var handle = handleGo.AddComponent<DoorHandle>();
            handle.Configure(hinge, 1f);
            return handle;
        }

        static Vector3 Round(float degrees) =>
            new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad) * 0.9f, 1f, Mathf.Sin(degrees * Mathf.Deg2Rad) * 0.9f);

        [Test]
        public void PullingTheHandleSwingsTheDoorWithTheHand()
        {
            var handle = Door(out var hinge);
            var hand = Hand(Round(0f));
            handle.Grab(hand);
            hand.transform.position = Round(30f);    // round the hinge, outwards
            handle.HoldUpdate(hand);
            Assert.AreEqual(30f, handle.Angle, 0.5f);
            Assert.AreEqual(30f, Quaternion.Angle(Quaternion.identity, hinge.localRotation), 0.5f);
            // The handle (a child of the leaf) is back under the hand.
            Assert.That(Vector3.Distance(handle.transform.position, hand.Position), Is.LessThan(0.01f));
        }

        [Test]
        public void DoorOpensOncePastTheThreshold()
        {
            var handle = Door(out _);
            int opened = 0;
            handle.Opened += _ => opened++;
            var hand = Hand(Round(0f));
            handle.Grab(hand);
            foreach (float deg in new[] { 20f, 60f, 80f, 50f })
            {
                hand.transform.position = Round(deg);
                handle.HoldUpdate(hand);
            }
            Assert.AreEqual(1, opened);
        }

        [Test]
        public void DoorCannotSwingThroughTheFrame()
        {
            var handle = Door(out _);
            var hand = Hand(Round(0f));
            handle.Grab(hand);
            hand.transform.position = Round(-40f);   // pushed inwards, behind the closed leaf
            handle.HoldUpdate(hand);
            Assert.AreEqual(0f, handle.Angle, 0.01f);
        }

        [Test]
        public void HeldObjectKeepsItsGripOffsetAndIsThrown()
        {
            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.transform.SetParent(_root.transform, false);
            cup.transform.position = new Vector3(0f, 1f, 0.1f);
            var grab = Grabbable.Make(cup, false, false);
            var hand = Hand(new Vector3(0f, 1f, 0f));
            grab.Grab(hand);
            Assert.IsTrue(grab.Held);
            hand.transform.position = new Vector3(1f, 1.5f, 0f);
            grab.HoldUpdate(hand);
            Assert.That(Vector3.Distance(cup.transform.position, new Vector3(1f, 1.5f, 0.1f)), Is.LessThan(0.001f));
            grab.Release(hand, new Vector3(0f, 0f, 30f));
            var rb = cup.GetComponent<Rigidbody>();
            Assert.IsFalse(grab.Held);
            Assert.IsFalse(rb.isKinematic);
            Assert.AreEqual(9f, rb.linearVelocity.magnitude, 0.01f);   // throws are capped
        }

        [Test]
        public void MakeFitsAColliderToTheMesh()
        {
            var go = new GameObject("plate");
            go.transform.SetParent(_root.transform, false);
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.transform.SetParent(go.transform, false);
            mesh.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);
            Grabbable.Make(go, true, true);
            var box = go.GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.AreEqual(0.3f, box.size.x, 0.001f);
            Assert.AreEqual(0.04f, box.size.y, 0.001f);   // never thinner than 4 cm, so a hand can close on it
        }
    }
}
