# SO-100 Visual Control MCP

This local stdio MCP server shortens the simulator loop to one screenshot call and one high-level pick/place call. The pick/place call converts pixels from the latest image to world targets, preflights every waypoint against the local SO-100 model, sends the motion sequence, and returns a new screenshot for visual verification.

## Visual-only boundary

The server captures only the `Robot Arm Studio` client window. It sends `POST /api/camera`, `POST /api/command`, and, on stop or recovery, `POST /api/control`. It never reads robot state, calibration, object positions, holding status, or the control panel's status text. Screenshots are the sole source for object selection and outcome verification.

The server returns a crop of the unobstructed 3D viewport, without the control panel or status readouts. Pixel projection uses the fixed camera presets in `OrbitCameraController` and the tabletop plane. Keep the preset angles and distances in sync if those values change. Observation IDs expire after two minutes and are invalidated by every action.

## Install

Requires Python 3.11 or newer. From the repository root in PowerShell:

```powershell
python -m venv mcp/so_arm100_visual/.venv
mcp/so_arm100_visual/.venv/Scripts/python.exe -m pip install -r mcp/so_arm100_visual/requirements.txt
codex mcp add so-arm100-visual -- "$PWD/mcp/so_arm100_visual/.venv/Scripts/python.exe" "$PWD/mcp/so_arm100_visual/server.py"
```

Restart Codex after registration so it starts the new MCP server. The Unity simulator must be running with its loopback API on port 8765. Override the API URL or window title with `ROBOT_ARM_API_URL` or `ROBOT_ARM_WINDOW_TITLE` in the MCP server environment if needed.

The repository contains the Skill source in `skills/so-arm100-visual-control/`. Install it to `%USERPROFILE%/.codex/skills/so-arm100-visual-control/` by running `skills/install-so-arm100-visual-control.ps1`, then restart Codex for skill discovery.

## Tools

- `observe_scene(view="oblique"|"overhead")`: set a calibrated view and return a screenshot plus a one-use observation ID.
- `pick_place_from_pixels(...)`: execute one visually selected cube transfer and return a verification screenshot. Each command is preflighted before the first motion; a rejected step stops the arm.
- `home_and_observe(...)`: home the arm, then return a screenshot. This opens the gripper.
- `resume_and_observe(...)`: clear pause/emergency stop without moving the arm, then return a screenshot.
- `stop_robot()`: send the simulator's stop control and invalidate old pixel coordinates.

Normal flow is two MCP calls. Use a third observation call only when the first view does not show the target clearly. The simulator uses kinematic waypoint motion and attaches a cube when the gripper closes within a distance threshold; it does not simulate finger contact forces or collision-free trajectory planning. Treat this as a control-pipeline prototype, not a real-robot safety proof.
