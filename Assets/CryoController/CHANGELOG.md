# Changelog

## 1.0.0

Initial release.

- CharacterController movement with acceleration, air control, coyote time, jump buffering and optional air jumps.
- Crouch (hold or toggle) with headroom checks, power slides, sprint with stamina, slope sliding.
- Mantling of ledges and ladders with a mantle exit at the top.
- Swimming in Water Volumes: dive, rise, float, wade and climb out, with no fall damage into deep water and an
  underwater screen tint.
- Moving platforms: the player rides anything that moves, with a MovingPlatform mover included.
- Mouse look with optional smoothing; gamepad look with a response curve and acceleration boost.
- Head bob, landing dip, step smoothing, strafe tilt, a sprint field-of-view boost and hold-to-zoom.
- A Reduce Camera Motion comfort setting, saved with the player's preferences and switchable in the pause menu.
- A Controller Only setting for gamepad-only play: the mouse is ignored and the pointer stays hidden, menus included.
- On-screen touch controls (Touch Controls) for phones and tablets: a floating stick, drag to look, and buttons.
- A controls panel (Controls Overlay) with every action's keys or gamepad buttons, lit while held; F1 toggles it.
- Movement profiles: swap the whole movement and camera feel at runtime, with Default, Realistic, Arcade and Tactical
  presets and a command that saves a controller's settings as a new profile.
- Rigidbody pushing.
- Interaction system with key-cap prompts, used on a press or, for objects that ask for it, a hold; plus
  Interactable, Toggle Interactable and Interactable Highlight components.
- Doors, gates and drawers (Door): hinged or sliding, swinging away from the player, pushing props aside and
  waiting for the player rather than pushing them, with locks, keys (Key Pickup) and a key ring (Player Keys).
- Short on-screen messages for the player (PlayerInteractor.Notify), such as "It's locked."
- Readable notes (Readable, Player Reader): pages on a paper card, turned with Interact.
- A Flashlight, switched with its own action, and a Held Item component that sways, bobs and lowers a model in view.
- Saving and loading (SaveSystem, ISaveable, SaveId): the player, doors, keys, carryables, notes and interactables
  save themselves, and the pause menu has Save and Load items when given a save slot.
- Pick up, carry, drop and throw rigidbodies with Player Carry and Carryable, and inspect them: bring one close and
  turn it over with the look input, with an optional description on screen.
- Lean and peek round corners, kept out of walls.
- Footstep, jump and landing audio, with per-surface sounds by marker, physics material, tag or terrain layer.
- Gamepad rumble on landings, mantles, throws and power slides.
- Camera shake (`AddCameraShake`) that settles back exactly where the player was looking, and a Player Fall Damage
  component for your health system.
- A look API: `Pitch`, `Yaw`, `LookAt` and `AddCameraKick` for recoil and hits.
- Pause menu with keyboard, mouse and gamepad navigation, an options page and a controls page with input rebinding; preferences and bindings saved.
  In web builds it opens when the browser frees the mouse on Escape, and has no Quit button.
- Player prefab, input actions asset and a playground demo scene that works in the Built-in Render Pipeline, URP and HDRP.
- Automated play mode and Edit mode tests for the Unity Test Framework.
- Works with Enter Play Mode Settings' domain reload turned off.
