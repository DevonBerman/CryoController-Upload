using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CryoController.Tests
{
    public class MovementTests
    {
        private TestRig rig;

        [SetUp]
        public void SetUp()
        {
            rig = new TestRig();
        }

        [TearDown]
        public void TearDown()
        {
            rig?.Dispose();
        }

        [UnityTest]
        public IEnumerator SettlesOntoTheGround()
        {
            BoxCollider ground = rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 0.3f, 0f));

            yield return new WaitForSeconds(0.5f);

            Assert.IsTrue(player.IsGrounded, "grounded");
            Assert.AreEqual(ground, player.Ground, "standing on the ground collider");
            Assert.AreEqual(0f, player.transform.position.y, 0.15f, "feet on the ground (within the capsule's skin width)");
        }

        [UnityTest]
        public IEnumerator JumpPeaksAtJumpHeight()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            yield return new WaitForSeconds(0.3f);
            int jumps = 0;
            player.Jumped += () => jumps++;
            float start = player.transform.position.y;
            float peak = start;

            yield return rig.Press(GamepadButton.South);
            float end = Time.time + 1.2f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, player.transform.position.y);
                yield return null;
            }

            Assert.AreEqual(1, jumps, "one jump");
            Assert.AreEqual(1.2f, peak - start, 0.1f, "peak height is the default Jump Height");
            Assert.IsTrue(player.IsGrounded, "landed again");
        }

        [UnityTest]
        public IEnumerator AirJumpClimbsHigherAndComesBackOnLanding()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            TestRig.SetField(player, "airJumps", 1);
            yield return new WaitForSeconds(0.3f);
            int jumps = 0;
            player.Jumped += () => jumps++;
            float start = player.transform.position.y;
            float peak = start;

            yield return rig.Press(GamepadButton.South);
            yield return new WaitForSeconds(0.25f);   // near the top of the first jump
            Assert.AreEqual(1, player.AirJumpsLeft, "one air jump left in the air");
            yield return rig.Press(GamepadButton.South);
            Assert.AreEqual(0, player.AirJumpsLeft, "used");
            float end = Time.time + 1.5f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, player.transform.position.y);
                yield return null;
            }

            Assert.AreEqual(2, jumps, "a jump and an air jump");
            Assert.Greater(peak - start, 1.7f, "higher than a single 1.2 m jump");
            Assert.IsTrue(player.IsGrounded, "landed");
            Assert.AreEqual(1, player.AirJumpsLeft, "the air jump comes back on landing");
        }

        [UnityTest]
        public IEnumerator NoAirJumpsByDefault()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            yield return new WaitForSeconds(0.3f);
            int jumps = 0;
            player.Jumped += () => jumps++;

            yield return rig.Press(GamepadButton.South);
            yield return new WaitForSeconds(0.25f);
            yield return rig.Press(GamepadButton.South);
            yield return new WaitForSeconds(1f);

            Assert.AreEqual(1, jumps, "the press in mid-air does nothing");
        }

        [UnityTest]
        public IEnumerator CoyoteTimeAcceptsALateJump()
        {
            BoxCollider ground = rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            TestRig.SetField(player, "coyoteTime", 0.5f);   // generous, so a slow frame cannot fail the test
            yield return new WaitForSeconds(0.3f);
            int jumps = 0;
            player.Jumped += () => jumps++;

            ground.enabled = false;   // the ground vanishes under the player
            yield return TestRig.WaitUntil(() => !player.IsGrounded, 0.5f);
            Assert.IsFalse(player.IsGrounded, "falling");
            yield return new WaitForSeconds(0.1f);
            yield return rig.Press(GamepadButton.South);

            Assert.AreEqual(1, jumps, "a jump shortly after leaving the ground is accepted");
            Assert.Greater(player.Velocity.y, 0f, "rising");
        }

        [UnityTest]
        public IEnumerator CoyoteTimeRunsOut()
        {
            BoxCollider ground = rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            TestRig.SetField(player, "coyoteTime", 0.1f);
            yield return new WaitForSeconds(0.3f);
            int jumps = 0;
            player.Jumped += () => jumps++;

            ground.enabled = false;
            yield return TestRig.WaitUntil(() => !player.IsGrounded, 0.5f);
            yield return new WaitForSeconds(0.4f);
            yield return rig.Press(GamepadButton.South);

            Assert.AreEqual(0, jumps, "too late: no jump");
        }

        [UnityTest]
        public IEnumerator JumpBufferJumpsOnLanding()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 1.5f, 0f));
            TestRig.SetField(player, "jumpBufferTime", 0.6f);   // longer than the fall of about 0.39 s
            int jumps = 0;
            player.Jumped += () => jumps++;
            yield return null;

            yield return rig.Press(GamepadButton.South);   // in mid-air, never grounded, so no coyote time either
            Assert.AreEqual(0, jumps, "no jump in the air");
            yield return TestRig.WaitUntil(() => jumps > 0, 1f);

            Assert.AreEqual(1, jumps, "the early press jumps on landing");
        }

        [UnityTest]
        public IEnumerator WithoutJumpBufferAnEarlyPressIsLost()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(new Vector3(0f, 1.5f, 0f));
            TestRig.SetField(player, "jumpBufferTime", 0f);
            int jumps = 0;
            player.Jumped += () => jumps++;
            yield return null;

            yield return rig.Press(GamepadButton.South);
            yield return new WaitForSeconds(1f);

            Assert.IsTrue(player.IsGrounded, "landed");
            Assert.AreEqual(0, jumps, "no jump");
        }

        [UnityTest]
        public IEnumerator HeadBobsWhileWalking()
        {
            rig.CreateGround(200f);
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            Transform eyes = player.GetComponentInChildren<Camera>(true).transform;
            yield return new WaitForSeconds(0.3f);

            float bob = 0f;
            yield return Walk(eyes, 1.5f, value => bob = value);

            Assert.Greater(bob, 0.015f, "the camera bobs by about the Head Bob Amplitude");
        }

        [UnityTest]
        public IEnumerator ReducedCameraMotionStopsTheBob()
        {
            rig.CreateGround(200f);
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            player.ReduceCameraMotion = true;
            Transform eyes = player.GetComponentInChildren<Camera>(true).transform;
            yield return new WaitForSeconds(0.3f);

            float bob = 0f;
            yield return Walk(eyes, 1.5f, value => bob = value);

            Assert.Less(bob, 0.002f, "the camera stays at eye height");
        }

        // Walks forward for the time and reports the camera's largest offset from its resting eye position.
        private IEnumerator Walk(Transform eyes, float seconds, System.Action<float> largestOffset)
        {
            rig.SetMove(Vector2.up);
            float largest = 0f;
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                Vector3 offset = eyes.localPosition - new Vector3(0f, TestRig.EyeHeight, 0f);
                largest = Mathf.Max(largest, offset.magnitude);
                yield return null;
            }
            rig.SetMove(Vector2.zero);
            largestOffset(largest);
        }

        [UnityTest]
        public IEnumerator CrouchStaysDownUnderACeiling()
        {
            rig.CreateGround();
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            CharacterController capsule = player.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.3f);

            rig.Hold(GamepadButton.East);
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(player.IsCrouching, "crouched");

            BoxCollider ceiling = rig.CreateBox("Ceiling", new Vector3(0f, 1.4f, 0f), new Vector3(4f, 0.2f, 4f));   // underside at 1.3 m
            rig.Hold(GamepadButton.East, false);
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(player.IsCrouching, "still crouched under the ceiling");
            Assert.Less(capsule.height, 1.3f, "the capsule fits under it");

            Object.DestroyImmediate(ceiling.gameObject);
            yield return new WaitForSeconds(0.4f);
            Assert.IsFalse(player.IsCrouching, "stood up once clear");
            Assert.AreEqual(TestRig.StandingHeight, capsule.height, 0.01f, "full height");
        }

        [UnityTest]
        public IEnumerator PowerSlideKeepsMomentumThenCrouches()
        {
            rig.CreateGround(300f);
            FirstPersonController player = rig.CreatePlayer(Vector3.zero);
            yield return new WaitForSeconds(0.3f);

            rig.SetMove(Vector2.up);
            rig.Hold(GamepadButton.LeftStick);   // sprint
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(player.IsSprinting, "sprinting");

            rig.Hold(GamepadButton.East);   // crouch while sprinting
            yield return TestRig.WaitUntil(() => player.IsPowerSliding, 0.5f);
            Assert.IsTrue(player.IsPowerSliding, "sliding");
            yield return null;
            Vector3 velocity = player.Velocity;
            velocity.y = 0f;
            Assert.Greater(velocity.magnitude, 7.5f, "faster than the sprint it started from");

            yield return TestRig.WaitUntil(() => !player.IsPowerSliding, 3f);
            Assert.IsFalse(player.IsPowerSliding, "the slide ran out");
            Assert.IsTrue(player.IsCrouching, "crouched after the slide");
        }
    }
}
