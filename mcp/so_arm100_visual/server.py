from __future__ import annotations

import asyncio
import ctypes
import io
import logging
import math
import os
import secrets
import sys
import time
from dataclasses import dataclass
from typing import Literal

import httpx
import mss
from mcp.server import MCPServer
from mcp.server.mcpserver import Image
from PIL import Image as PillowImage
from ctypes import wintypes


logging.basicConfig(stream=sys.stderr, level=logging.WARNING)
LOG = logging.getLogger("so_arm100_visual")

API_URL = os.environ.get("ROBOT_ARM_API_URL", "http://127.0.0.1:8765").rstrip("/")
WINDOW_TITLE = os.environ.get("ROBOT_ARM_WINDOW_TITLE", "Robot Arm Studio")
OBSERVATION_TTL_SECONDS = 120.0

# Fixed, source-controlled calibration for the Unity camera and table. These
# values mirror the two camera presets in OrbitCameraController/Bootstrap.
FOV_Y_DEG = 48.0
CAMERA_YAW_DEG = 24.0
CAMERA_FOCUS = (0.0, 0.55, 0.24)
VIEW_PRESETS = {
    "oblique": (24.0, 1.75),
    "overhead": (58.0, 2.05),
}
DEFAULT_PICK_CENTER_Y = 0.450
DEFAULT_PLACE_SURFACE_Y = 0.425
PAYLOAD_HALF_SIZE = 0.030
HELD_PAYLOAD_OFFSET = 0.070

# Static kinematic profile for preflight only. No scene or arm state is read.
ARM_BASE = (0.0, 0.42, 0.05)
UPPER_ARM_LENGTH = 0.135
FOREARM_LENGTH = 0.140
TOOL_LENGTH = 0.200
SHOULDER_HEIGHT = 0.080
TCP_OFFSET_Y = -0.010
JOINT_SPEED_DEG_PER_SECOND = 65.0
JOINT_MIN = (-115.0, -95.0, 0.0, -130.0, -180.0)
JOINT_MAX = (115.0, 95.0, 150.0, 130.0, 180.0)
WORKSPACE = ((-0.48, 0.48), (0.43, 0.78), (-0.35, 0.55))

server = MCPServer("SO-100 Visual Control")
_operation_lock = asyncio.Lock()


@dataclass(frozen=True)
class Observation:
    id: str
    created_at: float
    view: str
    image_width: int
    image_height: int
    camera_width: int
    capture_left: int
    pitch_deg: float
    distance: float
    image_bytes: bytes


_latest_observation: Observation | None = None


def _user32():
    return ctypes.windll.user32


def _enable_dpi_awareness() -> None:
    try:
        _user32().SetProcessDpiAwarenessContext(ctypes.c_void_p(0xFFFFFFFFFFFFFFFC))
    except (AttributeError, OSError):
        pass


_enable_dpi_awareness()


def _find_robot_window() -> int:
    user32 = _user32()
    matches: list[int] = []
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
    user32.GetWindowTextLengthW.restype = ctypes.c_int
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.restype = ctypes.c_int
    enum_windows = user32.EnumWindows
    enum_windows.argtypes = [ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM), wintypes.LPARAM]
    enum_windows.restype = wintypes.BOOL

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def visit(hwnd: int, _lparam: int) -> bool:
        if not user32.IsWindowVisible(hwnd):
            return True
        length = user32.GetWindowTextLengthW(hwnd)
        if length <= 0:
            return True
        title = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, title, length + 1)
        if WINDOW_TITLE.casefold() in title.value.casefold():
            matches.append(int(hwnd))
        return True

    enum_windows(visit, 0)
    if len(matches) != 1:
        raise RuntimeError(
            f"需要恰好一个标题包含 {WINDOW_TITLE!r} 的仿真窗口，当前找到 {len(matches)} 个。"
        )
    return matches[0]


def _activate_and_capture_client() -> tuple[bytes, int, int]:
    """Bring the existing simulator forward and capture only its client area."""
    user32 = _user32()
    hwnd = _find_robot_window()
    user32.IsIconic.argtypes = [wintypes.HWND]
    user32.IsIconic.restype = wintypes.BOOL
    user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.BringWindowToTop.argtypes = [wintypes.HWND]
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.GetClientRect.restype = wintypes.BOOL
    user32.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
    user32.ClientToScreen.restype = wintypes.BOOL
    if user32.IsIconic(hwnd):
        user32.ShowWindow(hwnd, 9)  # SW_RESTORE
    user32.BringWindowToTop(hwnd)
    user32.SetForegroundWindow(hwnd)

    rect = wintypes.RECT()
    if not user32.GetClientRect(hwnd, ctypes.byref(rect)):
        raise RuntimeError("无法读取仿真窗口的客户区大小。")
    width = rect.right - rect.left
    height = rect.bottom - rect.top
    if width < 500 or height < 400:
        raise RuntimeError(f"仿真窗口太小（{width}×{height}）；请先放大窗口再观察。")

    origin = wintypes.POINT(0, 0)
    if not user32.ClientToScreen(hwnd, ctypes.byref(origin)):
        raise RuntimeError("无法确定仿真窗口的屏幕位置。")
    time.sleep(0.15)
    if user32.GetForegroundWindow() != hwnd:
        raise RuntimeError("仿真窗口未能置前；请手动切到 Robot Arm Studio 后重试。")

    with mss.MSS() as screen:
        shot = screen.grab({"left": origin.x, "top": origin.y, "width": width, "height": height})
        shot_rgb = shot.rgb
    image = PillowImage.frombytes("RGB", shot.size, shot_rgb)
    output = io.BytesIO()
    image.save(output, format="JPEG", quality=88, optimize=True)
    image_bytes = output.getvalue()
    return image_bytes, width, height


async def _post_json(path: str, payload: dict, timeout: float = 5.0) -> dict:
    try:
        async with httpx.AsyncClient(timeout=timeout) as client:
            response = await client.post(f"{API_URL}{path}", json=payload)
        response.raise_for_status()
        body = response.json()
    except httpx.HTTPStatusError as exc:
        try:
            message = exc.response.json().get("error", exc.response.text)
        except Exception:
            message = exc.response.text
        raise RuntimeError(f"仿真接口拒绝命令：{message}") from exc
    except httpx.HTTPError as exc:
        raise RuntimeError(f"无法连接仿真接口 {API_URL}；请确认 Robot Arm Studio 正在运行。") from exc
    if not body.get("ok", False):
        raise RuntimeError(body.get("error") or "仿真接口未接受请求。")
    return body


async def _observe(view: str) -> tuple[Observation, list[Image | str]]:
    global _latest_observation
    if view not in VIEW_PRESETS:
        raise ValueError("view 必须是 oblique 或 overhead。")
    await _post_json("/api/camera", {"view": view})
    await asyncio.sleep(0.25)
    image_bytes, width, height = await asyncio.to_thread(_activate_and_capture_client)
    panel_width = min(470.0, max(350.0, width * 0.28))
    viewport_left = int(round(min(width * 0.90, 32.0 + panel_width)))
    frame = PillowImage.open(io.BytesIO(image_bytes)).convert("RGB")
    frame = frame.crop((viewport_left, 0, width, height))
    output = io.BytesIO()
    frame.save(output, format="JPEG", quality=88, optimize=True)
    image_bytes = output.getvalue()
    pitch, distance = VIEW_PRESETS[view]
    observation = Observation(
        id=secrets.token_urlsafe(12),
        created_at=time.monotonic(),
        view=view,
        image_width=frame.width,
        image_height=height,
        camera_width=width,
        capture_left=viewport_left,
        pitch_deg=pitch,
        distance=distance,
        image_bytes=image_bytes,
    )
    _latest_observation = observation
    text = (
        f"observation_id={observation.id}; image={observation.image_width}x{height} (3D 画面裁剪); "
        f"camera={view}. 之后的像素坐标按这张原尺寸截图填写。"
    )
    return observation, [text, Image(data=image_bytes, format="jpeg")]


def _require_current_observation(observation_id: str) -> Observation:
    observation = _latest_observation
    if observation is None or observation.id != observation_id:
        raise ValueError("截图标识已失效。请先调用 observe_scene 获取新图，再使用该图的像素坐标。")
    if time.monotonic() - observation.created_at > OBSERVATION_TTL_SECONDS:
        _clear_observation()
        raise ValueError("这张截图已超过 120 秒。请重新观察后再发动作。")
    return observation


def _clear_observation() -> None:
    global _latest_observation
    _latest_observation = None


def _pixel_to_plane(observation: Observation, pixel_x: int, pixel_y: int, plane_y: float) -> tuple[float, float]:
    width = observation.image_width
    height = observation.image_height
    if not (0 <= pixel_x < observation.image_width and 0 <= pixel_y < height):
        raise ValueError(f"像素 ({pixel_x}, {pixel_y}) 不在返回的 3D 截图范围内。")

    # The returned screenshot is cropped to the unobstructed camera region,
    # while the Unity camera projection still spans the full client window.
    aspect = observation.camera_width / height
    camera_pixel_x = pixel_x + observation.capture_left
    ndc_x = 2.0 * camera_pixel_x / observation.camera_width - 1.0
    ndc_y = 1.0 - 2.0 * pixel_y / height
    tangent = math.tan(math.radians(FOV_Y_DEG) * 0.5)
    ray_local_x = ndc_x * tangent * aspect
    ray_local_y = ndc_y * tangent

    yaw = math.radians(CAMERA_YAW_DEG)
    pitch = math.radians(observation.pitch_deg)
    sin_yaw, cos_yaw = math.sin(yaw), math.cos(yaw)
    sin_pitch, cos_pitch = math.sin(pitch), math.cos(pitch)

    # Right/up/forward axes for Quaternion.Euler(pitch, yaw, 0) in Unity.
    right_axis = (cos_yaw, 0.0, -sin_yaw)
    up_axis = (sin_yaw * sin_pitch, cos_pitch, cos_yaw * sin_pitch)
    forward_axis = (sin_yaw * cos_pitch, -sin_pitch, cos_yaw * cos_pitch)
    direction = tuple(
        right_axis[i] * ray_local_x + up_axis[i] * ray_local_y + forward_axis[i]
        for i in range(3)
    )
    distance = observation.distance
    camera_origin = (
        CAMERA_FOCUS[0] - sin_yaw * cos_pitch * distance,
        CAMERA_FOCUS[1] + sin_pitch * distance,
        CAMERA_FOCUS[2] - cos_yaw * cos_pitch * distance,
    )
    if direction[1] >= -1e-6:
        raise ValueError("所选像素的视线没有与桌面相交；请在工作台范围内选点。")
    ray_distance = (plane_y - camera_origin[1]) / direction[1]
    if ray_distance <= 0:
        raise ValueError("所选像素位于相机后方；请重新观察并选择工作台上的点。")
    return (
        camera_origin[0] + direction[0] * ray_distance,
        camera_origin[2] + direction[2] * ray_distance,
    )


def _validate_workspace(target: tuple[float, float, float]) -> None:
    for axis, value, limits in zip("XYZ", target, WORKSPACE):
        if not math.isfinite(value) or value < limits[0] or value > limits[1]:
            raise ValueError(
                f"推算出的 {axis}={value:.3f} m 超出机械臂工作空间 "
                f"[{limits[0]:.2f}, {limits[1]:.2f}] m；未执行任何动作。"
            )


def _solve_ik(target: tuple[float, float, float]) -> tuple[float, ...]:
    _validate_workspace(target)
    dx = target[0] - ARM_BASE[0]
    dy = target[1] - ARM_BASE[1]
    dz = target[2] - ARM_BASE[2]
    yaw = math.degrees(math.atan2(dx, dz))
    radial_to_tcp = math.hypot(dx, dz)
    radial = radial_to_tcp - TOOL_LENGTH
    vertical = dy - SHOULDER_HEIGHT - TCP_OFFSET_Y
    distance = math.hypot(radial, vertical)
    min_reach = abs(UPPER_ARM_LENGTH - FOREARM_LENGTH) + 0.012
    max_reach = UPPER_ARM_LENGTH + FOREARM_LENGTH - 0.012
    if radial < 0.012 or distance < min_reach or distance > max_reach:
        raise ValueError(f"目标 {target!r} 在 SO-100 的近似可达范围之外；未执行任何动作。")

    phi = math.atan2(vertical, radial)
    cos_shoulder = (UPPER_ARM_LENGTH**2 + distance**2 - FOREARM_LENGTH**2) / (
        2.0 * UPPER_ARM_LENGTH * distance
    )
    cos_elbow = (UPPER_ARM_LENGTH**2 + FOREARM_LENGTH**2 - distance**2) / (
        2.0 * UPPER_ARM_LENGTH * FOREARM_LENGTH
    )
    shoulder_math = phi + math.acos(min(1.0, max(-1.0, cos_shoulder)))
    elbow_internal = math.acos(min(1.0, max(-1.0, cos_elbow)))
    elbow_relative = -(math.pi - elbow_internal)
    shoulder_deg = -math.degrees(shoulder_math)
    elbow_deg = -math.degrees(elbow_relative)
    wrist_deg = -(shoulder_deg + elbow_deg)
    joints = (yaw, shoulder_deg, elbow_deg, wrist_deg, 0.0)
    for name, value, low, high in zip(
        ("shoulder_pan", "shoulder_lift", "elbow_flex", "wrist_flex", "wrist_roll"),
        joints,
        JOINT_MIN,
        JOINT_MAX,
    ):
        if value < low or value > high:
            raise ValueError(
                f"目标需要 {name}={value:.1f}°，超出限位 [{low:.0f}°, {high:.0f}°]；未执行任何动作。"
            )
    return joints


def _approach_target(x: float, z: float, center_y: float) -> tuple[float, float, float]:
    highest = min(WORKSPACE[1][1], center_y + 0.22)
    lowest = max(WORKSPACE[1][0], center_y + 0.050)
    count = max(1, int((highest - lowest) / 0.01) + 1)
    for i in range(count + 1):
        y = highest - i * 0.01
        if y < lowest:
            break
        target = (x, round(y, 3), z)
        try:
            _solve_ik(target)
            return target
        except ValueError:
            continue
    raise ValueError(f"点 ({x:.3f}, {z:.3f}) 没有高于物体 5 cm 的安全接近位姿；未执行任何动作。")


def _compensate_held_offset(x: float, z: float) -> tuple[float, float]:
    """Place the held cube center at x/z despite the simulation's +Z 7 cm grip offset."""
    tx, tz = x, z
    for _ in range(4):
        dx = tx - ARM_BASE[0]
        dz = tz - ARM_BASE[2]
        length = math.hypot(dx, dz)
        if length < 1e-6:
            raise ValueError("放置点太靠近机械臂底座，无法补偿夹爪偏移。")
        tx = x - HELD_PAYLOAD_OFFSET * dx / length
        tz = z - HELD_PAYLOAD_OFFSET * dz / length
    return tx, tz


async def _move(target: tuple[float, float, float], previous: tuple[float, ...] | None) -> tuple[float, ...]:
    joints = _solve_ik(target)
    if previous is None:
        # Conservative first segment: user input may have moved the simulated arm.
        settle_seconds = 3.6
    else:
        largest_delta = max(abs((new - old + 180.0) % 360.0 - 180.0) for new, old in zip(joints, previous))
        settle_seconds = largest_delta / JOINT_SPEED_DEG_PER_SECOND + 0.30
    await _post_json("/api/command", {"action": "MoveTcp", "arm": 0, "target": list(target)})
    await asyncio.sleep(settle_seconds)
    return joints


async def _stop_best_effort() -> None:
    try:
        await _post_json("/api/control", {"action": "stop"}, timeout=1.0)
    except Exception as exc:
        LOG.warning("Could not send stop after an incomplete sequence: %s", exc)


@server.tool()
async def observe_scene(view: Literal["oblique", "overhead"] = "oblique") -> list[Image | str]:
    """Capture a fresh simulator screenshot and observation_id for visual targeting.

    Use the oblique view first. Switch to overhead only if an object or target is
    hidden or ambiguous. This tool returns pixels and metadata, not scene state.
    """
    async with _operation_lock:
        _, result = await _observe(view)
        return result


@server.tool()
async def pick_place_from_pixels(
    observation_id: str,
    pick_x: int,
    pick_y: int,
    place_x: int,
    place_y: int,
    pick_center_height: float = DEFAULT_PICK_CENTER_Y,
    place_surface_height: float = DEFAULT_PLACE_SURFACE_Y,
) -> list[Image | str]:
    """Pick one visually selected payload and place it at a visually selected point.

    Pixel inputs must come from the latest observe_scene screenshot. The tool
    computes screen-to-table projection from fixed camera calibration, checks
    the workspace and SO-100 IK locally, executes approach/grasp/lift/place waypoints,
    then returns a new screenshot for visual verification. It never reads scene state.
    The recorded camera preset is restored before movement so the pixel calibration stays valid.
    """
    global _latest_observation
    async with _operation_lock:
        observation = _require_current_observation(observation_id)
        if not 0.43 <= pick_center_height <= 0.78:
            raise ValueError("pick_center_height 必须在 0.43–0.78 m 范围内。")
        if not 0.423 <= place_surface_height <= 0.75:
            raise ValueError("place_surface_height 必须在 0.423–0.75 m 范围内。")

        # Use the visual center of the picked cube, and the visible supporting
        # surface at the destination (target pad or top of another cube).
        pick_xz = _pixel_to_plane(observation, pick_x, pick_y, pick_center_height)
        place_xz = _pixel_to_plane(observation, place_x, place_y, place_surface_height)
        pick = (pick_xz[0], pick_center_height, pick_xz[1])
        place_center_y = place_surface_height + PAYLOAD_HALF_SIZE
        place_grip_xz = _compensate_held_offset(place_xz[0], place_xz[1])
        place = (place_grip_xz[0], place_center_y, place_grip_xz[1])

        # Fully plan and validate every waypoint before moving anything.
        pick_approach = _approach_target(pick[0], pick[2], pick[1])
        place_approach = _approach_target(place[0], place[2], place[1])
        for target in (pick_approach, pick, place_approach, place):
            _solve_ik(target)
        await _post_json("/api/camera", {"view": observation.view})
        await asyncio.sleep(0.25)
        _latest_observation = None

        steps: list[tuple[str, dict, float]] = [
            ("Gripper", {"open": 1.0}, 0.30),
            ("MoveTcp", {"target": list(pick_approach)}, 0.0),
            ("MoveTcp", {"target": list(pick)}, 0.0),
            ("Gripper", {"open": 0.0}, 0.35),
            ("MoveTcp", {"target": list(pick_approach)}, 0.0),
            ("MoveTcp", {"target": list(place_approach)}, 0.0),
            ("MoveTcp", {"target": list(place)}, 0.0),
            ("Gripper", {"open": 1.0}, 0.45),
            ("MoveTcp", {"target": list(place_approach)}, 0.0),
        ]
        previous_joints: tuple[float, ...] | None = None
        accepted = 0
        try:
            for action, args, delay in steps:
                if action == "MoveTcp":
                    previous_joints = await _move(tuple(args["target"]), previous_joints)
                else:
                    await _post_json("/api/command", {"action": action, "arm": 0, **args})
                    await asyncio.sleep(delay)
                accepted += 1
            await asyncio.sleep(0.35)
            _, result = await _observe(observation.view)
            result[0] = (
                f"已发送 {accepted} 个动作步骤；起点像素→世界约 ({pick[0]:.3f}, {pick[1]:.3f}, {pick[2]:.3f}) m，"
                f"目标像素→夹爪目标约 ({place[0]:.3f}, {place[1]:.3f}, {place[2]:.3f}) m。"
                "请只根据返回截图判断抓取和放置是否成功；命令已接受本身不代表成功。\n"
                + str(result[0])
            )
            return result
        except asyncio.CancelledError:
            await _stop_best_effort()
            _clear_observation()
            raise
        except Exception:
            await _stop_best_effort()
            _clear_observation()
            raise


@server.tool()
async def home_and_observe(view: Literal["oblique", "overhead"] = "oblique") -> list[Image | str]:
    """Return the gripper to the simulation's home pose, then capture a screenshot."""
    global _latest_observation
    async with _operation_lock:
        _latest_observation = None
        await _post_json("/api/command", {"action": "Resume", "arm": 0})
        await _post_json("/api/command", {"action": "Home", "arm": 0})
        await asyncio.sleep(3.2)
        _, result = await _observe(view)
        return result


@server.tool()
async def resume_and_observe(view: Literal["oblique", "overhead"] = "oblique") -> list[Image | str]:
    """Resume a paused or stopped simulation without moving the arm, then return a screenshot."""
    global _latest_observation
    async with _operation_lock:
        _latest_observation = None
        await _post_json("/api/command", {"action": "Resume", "arm": 0})
        await asyncio.sleep(0.15)
        _, result = await _observe(view)
        return result


@server.tool()
async def stop_robot() -> str:
    """Immediately stop the simulated arm and invalidate any old screenshot coordinates."""
    _clear_observation()
    await _post_json("/api/control", {"action": "stop"})
    return "仿真急停已发送。用 resume_and_observe 恢复，或用 home_and_observe 回零；旧像素坐标已失效。"


if __name__ == "__main__":
    server.run()
