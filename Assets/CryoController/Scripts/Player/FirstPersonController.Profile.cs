using UnityEngine;

namespace CryoController
{
    // FirstPersonController: movement profiles, which swap the movement and camera-feel settings as a set. The settings,
    // state and frame loop are in FirstPersonController.cs.
    public partial class FirstPersonController
    {
        /// <summary>
        /// The <see cref="MovementProfile"/> in use, or null for the settings authored on this component. Setting it
        /// applies the profile's settings at once, and null puts the authored ones back: swap profiles for states such as
        /// wounded, carrying something heavy or wading.
        /// </summary>
        public MovementProfile Profile
        {
            get { return profile; }
            set
            {
                profile = value;
                if (authoredSettings != null)   // before Awake, Awake applies it
                {
                    ApplyProfile();
                }
            }
        }

        // Called first thing in Awake: keeps the authored settings, then applies the profile over them.
        private void InitializeProfile()
        {
            authoredSettings = MovementProfile.CreateFrom(this);
            authoredSettings.hideFlags = HideFlags.HideAndDontSave;
            ApplyProfile();
        }

        private void ApplyProfile()
        {
            TransferProfile(profile != null ? profile : authoredSettings, true);
            appliedProfile = profile;
            appliedProfileVersion = profile != null ? profile.Version : 0;
        }

#if UNITY_EDITOR
        // A profile assigned or edited in the Inspector during Play Mode takes effect at once.
        private void FollowProfileEdits()
        {
            // A profile destroyed while in use (one made at runtime) would leave its settings behind.
            if (!ReferenceEquals(appliedProfile, null) && appliedProfile == null)
            {
                profile = null;
                ApplyProfile();
                return;
            }
            if (authoredSettings != null
                && (profile != appliedProfile || (profile != null && profile.Version != appliedProfileVersion)))
            {
                ApplyProfile();
            }
        }
#endif

        // Copies every profile setting from the profile into this component (intoController) or the other way. One list
        // for both directions, so they cannot drift apart.
        internal void TransferProfile(MovementProfile profile, bool intoController)
        {
            Transfer(ref walkSpeed, ref profile.walkSpeed, intoController);
            Transfer(ref sprintSpeed, ref profile.sprintSpeed, intoController);
            Transfer(ref crouchSpeed, ref profile.crouchSpeed, intoController);
            Transfer(ref groundAcceleration, ref profile.groundAcceleration, intoController);
            Transfer(ref groundDeceleration, ref profile.groundDeceleration, intoController);
            Transfer(ref airControl, ref profile.airControl, intoController);
            Transfer(ref jumpHeight, ref profile.jumpHeight, intoController);
            Transfer(ref gravity, ref profile.gravity, intoController);
            Transfer(ref coyoteTime, ref profile.coyoteTime, intoController);
            Transfer(ref jumpBufferTime, ref profile.jumpBufferTime, intoController);
            Transfer(ref airJumps, ref profile.airJumps, intoController);
            Transfer(ref airJumpHeight, ref profile.airJumpHeight, intoController);
            Transfer(ref maxSlideSpeed, ref profile.maxSlideSpeed, intoController);
            Transfer(ref slideControl, ref profile.slideControl, intoController);
            Transfer(ref mantleMaxHeight, ref profile.mantleMaxHeight, intoController);
            Transfer(ref autoMantleHeight, ref profile.autoMantleHeight, intoController);
            Transfer(ref mantleDuration, ref profile.mantleDuration, intoController);
            Transfer(ref climbSpeed, ref profile.climbSpeed, intoController);
            Transfer(ref ladderDismountSpeed, ref profile.ladderDismountSpeed, intoController);
            Transfer(ref crouchTransitionSpeed, ref profile.crouchTransitionSpeed, intoController);
            Transfer(ref powerSlide, ref profile.powerSlide, intoController);
            Transfer(ref powerSlideBoost, ref profile.powerSlideBoost, intoController);
            Transfer(ref powerSlideFriction, ref profile.powerSlideFriction, intoController);
            Transfer(ref powerSlideMinSpeed, ref profile.powerSlideMinSpeed, intoController);
            Transfer(ref powerSlideSteering, ref profile.powerSlideSteering, intoController);
            Transfer(ref headBob, ref profile.headBob, intoController);
            Transfer(ref headBobAmplitude, ref profile.headBobAmplitude, intoController);
            Transfer(ref headBobSway, ref profile.headBobSway, intoController);
            Transfer(ref headBobStepLength, ref profile.headBobStepLength, intoController);
            Transfer(ref landingDip, ref profile.landingDip, intoController);
            Transfer(ref smoothSteps, ref profile.smoothSteps, intoController);
            Transfer(ref sprintFovBoost, ref profile.sprintFovBoost, intoController);
            Transfer(ref strafeTilt, ref profile.strafeTilt, intoController);
            Transfer(ref zoomFovScale, ref profile.zoomFovScale, intoController);
            Transfer(ref zoomTime, ref profile.zoomTime, intoController);
            Transfer(ref leanDistance, ref profile.leanDistance, intoController);
            Transfer(ref leanAngle, ref profile.leanAngle, intoController);
            Transfer(ref leanTime, ref profile.leanTime, intoController);
        }

        private static void Transfer<T>(ref T setting, ref T profileSetting, bool intoController)
        {
            if (intoController)
            {
                setting = profileSetting;
            }
            else
            {
                profileSetting = setting;
            }
        }
    }
}
