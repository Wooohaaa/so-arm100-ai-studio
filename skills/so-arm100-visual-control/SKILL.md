---
name: so-arm100-visual-control
description: Control the SO-100 Unity simulator through screenshot-based observation, pixel-targeted pick-and-place actions, and visual verification.
---

# SO-100 Visual Control

Use this skill for tasks in the Robot Arm Studio simulator. Perception and success checks must come from rendered screenshots. The MCP server sends motion commands, but it does not read object or robot state from the simulation API.

## Fast visual workflow

1. Call `observe_scene` with the default `oblique` view. This is usually enough; use `overhead` only when an object or target is obscured or ambiguous.
2. Identify the payload and destination in that exact returned 3D-only image. Use the visual center of the payload and the center of the visible destination surface. Coordinates are pixels relative to the cropped image, whose width and height are included with `observation_id`.
3. Call `pick_place_from_pixels` once with that observation ID and the two pixel points. It plans an approach at least 5 cm above the object when the robot's modeled joint limits allow it, then grasps, lifts, transfers, releases, and retracts. It rejects stale screenshots, out-of-workspace points, and unreachable waypoints before moving.
4. Judge the returned screenshot yourself. Confirm visually that the payload moved to the intended surface and that the gripper cleared it. An accepted HTTP command is not evidence that an object was grasped or placed.

For a standard tabletop cube, keep the default `pick_center_height` (0.450 m) and `place_surface_height` (0.425 m). When stacking, pass the visible support surface's estimated height as `place_surface_height`; when picking a stacked cube, pass its center height as `pick_center_height`.

If the arm pose is uncertain, use `home_and_observe` first. Homing opens the gripper and releases any held object, so use it only when that is appropriate. If the motion looks unsafe, call `stop_robot`; then use `resume_and_observe` to continue from the stopped pose or `home_and_observe` to return to the home pose before any further pixel-based action.

## Visual-only constraints

- Never use `/api/state`, `/api/calibration`, UI status text, TCP readouts, or object-coordinate logs to locate a payload or decide that a task succeeded.
- Never reuse pixel coordinates after a motion, a new screenshot, a view change, or an expired observation ID.
- If the target is unclear, request a fresh screenshot from `overhead`; do not guess coordinates from memory.
- If preflight rejects a target, reobserve and choose a different visible point. Do not repeatedly resend the same rejected action.
- The simulator uses simplified kinematics and proximity-based grasp attachment. It is not a force/contact-accurate physics simulation and this workflow does not certify real hardware safety.
