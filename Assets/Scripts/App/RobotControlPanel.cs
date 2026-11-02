using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotArmStudio
{
    public sealed class RobotControlPanel : MonoBehaviour
    {
        private sealed class ChatLine
        {
            public string Role;
            public string Text;
        }

        private RobotFleet _fleet;
        private LocalRobotApi _api;
        private AiLocalSettings _settings;
        private readonly List<ChatLine> _chat = new List<ChatLine>();
        private string _apiKey = "";
        private string _instruction = "";
        private string _notice = "仿真已就绪。按 V 切换斜视/俯视，右键拖动画面，滚轮缩放。";
        private Vector2 _chatScroll;
        private Vector2 _uiScroll;
        private bool _showSettings;
        private bool _busy;
        private string _llmStatus = "未连接";
        private GUIStyle _panelStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _buttonStyle;
        private Texture2D _panelTexture;
        private Texture2D _cardTexture;
        private Texture2D _buttonTexture;
        private Texture2D _dangerTexture;
        private Font _uiFont;
        private Coroutine _planCoroutine;

        public void Configure(RobotFleet fleet, LocalRobotApi api)
        {
            _fleet = fleet;
            _api = api;
            _settings = RobotAiPlanner.LoadSettings();
            _apiKey = System.Environment.GetEnvironmentVariable("ROBOT_LLM_API_KEY") ?? "";
            AddChat("系统", "工作空间已启动。输入任务，AI 会规划并执行受限的仿真动作。");
        }

        private void OnGUI()
        {
            EnsureStyles();
            float width = Mathf.Clamp(Screen.width * 0.28f, 350f, 470f);
            float height = Screen.height - 32f;
            GUI.Box(new Rect(16f, 16f, width, height), GUIContent.none, _panelStyle);
            GUILayout.BeginArea(new Rect(32f, 26f, width - 32f, height - 20f));
            GUILayout.BeginVertical();
            _uiScroll = GUILayout.BeginScrollView(_uiScroll, GUILayout.Height(height - 24f));

            GUILayout.Label("ROBOT ARM STUDIO", _titleStyle);
            GUILayout.Label("机械臂 AI 控制台  ·  仅仿真", _smallStyle);
            GUILayout.Space(8f);
            DrawStatusCards();
            GUILayout.Space(8f);
            DrawQuickControls();
            GUILayout.Space(8f);
            DrawChat();
            GUILayout.Space(8f);
            DrawComposer();
            GUILayout.Space(8f);
            DrawSettings();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawStatusCards()
        {
            GUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("运行状态", _bodyStyle);
            string state = _fleet == null ? "初始化中" : _fleet.EmergencyStopped ? "急停" : _fleet.IsPaused ? "已暂停" : "运行中";
            GUILayout.Label("仿真  " + state + "      AI  " + _llmStatus, _smallStyle);
            GUILayout.Label("HTTP  " + (_api != null ? _api.Status : "未启动"), _smallStyle);
            if (_fleet != null)
            {
                foreach (var arm in _fleet.Arms)
                {
                    if (arm == null) continue;
                    Vector3 p = arm.Tcp;
                    GUILayout.Label($"{arm.Name}  {arm.Status}   TCP ({p.x:F2}, {p.y:F2}, {p.z:F2}) m", _smallStyle);
                    GUILayout.Label("夹爪 " + (arm.GripperOpen > 0.5f ? "打开" : "闭合") +
                                    (string.IsNullOrEmpty(arm.Holding) ? "" : " · 抓取 " + arm.Holding), _smallStyle);
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawQuickControls()
        {
            GUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("机械臂控制", _bodyStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("回零", _buttonStyle, GUILayout.Height(34)))
                Dispatch(new ApiCommand { action = "Home", arm = 0 });
            if (GUILayout.Button("松开", _buttonStyle, GUILayout.Height(34)))
                Dispatch(new ApiCommand { action = "Gripper", arm = 0, open = 1f });
            if (GUILayout.Button("夹紧", _buttonStyle, GUILayout.Height(34)))
                Dispatch(new ApiCommand { action = "Gripper", arm = 0, open = 0f });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (_fleet != null && _fleet.EmergencyStopped)
            {
                if (GUILayout.Button("恢复", _buttonStyle, GUILayout.Height(34f))) _fleet.ResumeAll();
            }
            else if (_fleet != null && _fleet.IsPaused)
            {
                if (GUILayout.Button("继续", _buttonStyle, GUILayout.Height(34))) _fleet.ResumeAll();
            }
            else
            {
                if (GUILayout.Button("暂停", _buttonStyle, GUILayout.Height(34))) _fleet?.Pause();
            }
            GUI.backgroundColor = new Color(0.78f, 0.22f, 0.24f);
            if (GUILayout.Button("急停", _buttonStyle, GUILayout.Height(34)))
            {
                _fleet?.StopAll();
                CancelActivePlan();
                _notice = "急停已触发。按“恢复”解除急停。";
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawChat()
        {
            GUILayout.BeginVertical(_cardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("指令与执行记录", _bodyStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("清空", GUILayout.Width(48))) _chat.Clear();
            GUILayout.EndHorizontal();
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(Mathf.Clamp(Screen.height * 0.24f, 145f, 250f)));
            foreach (var line in _chat)
            {
                GUILayout.Label(line.Role, _smallStyle);
                GUILayout.Label(line.Text, _bodyStyle);
                GUILayout.Space(5f);
            }
            GUILayout.EndScrollView();
            GUILayout.Label(_notice, _smallStyle);
            GUILayout.EndVertical();
        }

        private void DrawComposer()
        {
            GUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("用自然语言下达任务", _bodyStyle);
            _instruction = GUILayout.TextArea(_instruction, _inputStyle, GUILayout.Height(60f));
            GUI.enabled = !_busy;
            if (GUILayout.Button(_busy ? "AI 正在规划 / 执行…" : "发送给 AI 并执行", _buttonStyle, GUILayout.Height(38f)))
                SendInstruction();
            GUI.enabled = true;
            GUILayout.EndVertical();
        }

        private void DrawSettings()
        {
            GUILayout.BeginVertical(_cardStyle);
            _showSettings = GUILayout.Toggle(_showSettings, "AI 设置  ·  " + (_showSettings ? "收起" : "配置接口"), _bodyStyle);
            if (_showSettings)
            {
                GUILayout.Label("Base URL", _smallStyle);
                _settings.baseUrl = GUILayout.TextField(_settings.baseUrl ?? "", _inputStyle);
                GUILayout.Label("模型 ID", _smallStyle);
                _settings.model = GUILayout.TextField(_settings.model ?? "", _inputStyle);
                GUILayout.Label("API Key（仅保存在当前运行进程）", _smallStyle);
                _apiKey = GUILayout.PasswordField(_apiKey ?? "", '*', _inputStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("保存地址与模型", _buttonStyle, GUILayout.Height(31f)))
                {
                    RobotAiPlanner.SaveSettings(_settings);
                    _notice = "接口地址和模型 ID 已保存到本机 Unity Preferences；密钥未保存。";
                }
                if (GUILayout.Button("清除密钥", _buttonStyle, GUILayout.Height(31f))) _apiKey = "";
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void SendInstruction()
        {
            string text = (_instruction ?? "").Trim();
            if (string.IsNullOrEmpty(text))
            {
                _notice = "先输入一条任务指令。";
                return;
            }
            if (_fleet == null || _fleet.IsPaused || _fleet.EmergencyStopped)
            {
                _notice = "仿真当前暂停或急停，请先恢复。";
                return;
            }

            AddChat("你", text);
            _planCoroutine = StartCoroutine(RequestAndExecute(text));
        }

        private IEnumerator RequestAndExecute(string instruction)
        {
            _busy = true;
            _llmStatus = "规划中";
            _notice = "正在请求模型并验证动作…";
            _api?.SetLlmStatus(_llmStatus);

            AiPlanResult result = null;
            RobotStateSnapshot snapshot = _fleet.CreateSnapshot(_llmStatus);
            yield return RobotAiPlanner.RequestPlan(
                _settings.baseUrl, _settings.model, _apiKey, instruction, snapshot,
                _fleet.DescribePayloads(), BuildHistoryContext(), value => result = value);

            if (result == null || !result.ok)
            {
                _llmStatus = "请求失败";
                _notice = result != null ? result.error : "模型没有返回结果。";
                AddChat("AI", _notice);
                FinishRequest();
                yield break;
            }

            if (!_fleet.ValidateAiPlan(result.plan, out string planError))
            {
                _llmStatus = "计划被拒绝";
                _notice = "计划未执行：" + planError;
                AddChat("控制器", _notice);
                FinishRequest();
                yield break;
            }

            string summary = string.IsNullOrWhiteSpace(result.plan.summary) ? "已生成动作计划。" : result.plan.summary;
            AddChat("AI 计划", BuildPlanDescription(summary, result.plan));
            _llmStatus = "执行中";
            _api?.SetLlmStatus(_llmStatus);

            bool planCompleted = true;
            for (int i = 0; i < result.plan.actions.Length; i++)
            {
                if (_fleet.EmergencyStopped)
                {
                    _notice = "急停已触发，AI 计划中止。";
                    planCompleted = false;
                    break;
                }

                RobotAction action = result.plan.actions[i];
                if (action.type == "wait")
                {
                    _notice = $"等待 {action.waitSeconds:F1} 秒…";
                    yield return new WaitForSeconds(action.waitSeconds);
                    continue;
                }

                if (!_fleet.ExecuteAiAction(action, out string reason))
                {
                    _notice = "动作未执行：" + reason;
                    AddChat("控制器", _notice);
                    planCompleted = false;
                    break;
                }

                if (action.type == "move" || action.type == "joints")
                {
                    if (!_fleet.TryGetArm(action.arm, out IRobotArmDriver arm))
                    {
                        _notice = "动作未执行：机械臂编号无效。";
                        planCompleted = false;
                        break;
                    }
                    float elapsed = 0f;
                    while (arm.IsMoving && elapsed < 20f && !_fleet.EmergencyStopped && !_fleet.IsPaused)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                    if (_fleet.EmergencyStopped || _fleet.IsPaused)
                    {
                        planCompleted = false;
                        break;
                    }
                    if (elapsed >= 20f)
                    {
                        _notice = "动作超过 20 秒，已停止后续计划。";
                        AddChat("控制器", _notice);
                        planCompleted = false;
                        break;
                    }
                }
                else yield return null;
            }

            _llmStatus = _fleet.EmergencyStopped ? "已急停" : "就绪";
            if (_fleet.EmergencyStopped) _notice = "急停已触发，AI 计划中止。";
            else if (_fleet.IsPaused) _notice = "仿真已暂停，AI 计划中止。";
            else if (planCompleted) _notice = "计划执行完成。";
            _api?.SetLlmStatus(_llmStatus);
            FinishRequest();
        }

        private void FinishRequest()
        {
            _busy = false;
            _planCoroutine = null;
            if (_fleet != null && _fleet.EmergencyStopped) _llmStatus = "已急停";
            else if (_llmStatus != "请求失败") _llmStatus = "就绪";
            _api?.SetLlmStatus(_llmStatus);
        }

        private void Dispatch(ApiCommand command)
        {
            if (_fleet == null) return;
            if (_fleet.Dispatch(command, out string reason)) _notice = reason;
            else _notice = "命令被拒绝：" + reason;
        }

        private void AddChat(string role, string text)
        {
            _chat.Add(new ChatLine { Role = role, Text = text });
            while (_chat.Count > 36) _chat.RemoveAt(0);
            _chatScroll.y = float.MaxValue;
        }

        private string BuildHistoryContext()
        {
            if (_chat.Count <= 1) return "";
            int first = Mathf.Max(0, _chat.Count - 9);
            var builder = new System.Text.StringBuilder();
            for (int i = first; i < _chat.Count - 1; i++)
                builder.Append(_chat[i].Role).Append("：").Append(_chat[i].Text).Append("\n");
            return builder.ToString();
        }

        private static string BuildPlanDescription(string summary, RobotActionPlan plan)
        {
            var builder = new System.Text.StringBuilder(summary);
            builder.Append($"（{plan.actions.Length} 个动作，已通过坐标、可达性与动作检查）");
            for (int i = 0; i < plan.actions.Length; i++)
            {
                RobotAction action = plan.actions[i];
                builder.Append("\n").Append(i + 1).Append(". ");
                if (action.type == "move" && RobotSafety.TryReadTarget(action.target, out Vector3 target))
                    builder.Append($"机械臂 {action.arm + 1} 移动到 ({target.x:F2}, {target.y:F2}, {target.z:F2}) m");
                else if (action.type == "gripper") builder.Append(action.open ? "打开夹爪" : "闭合夹爪");
                else if (action.type == "home") builder.Append($"机械臂 {action.arm + 1} 回零");
                else if (action.type == "wait") builder.Append($"等待 {action.waitSeconds:F1} 秒");
                else if (action.type == "stop") builder.Append("触发急停");
                else builder.Append(action.type);
            }
            return builder.ToString();
        }

        private void CancelActivePlan()
        {
            if (_planCoroutine != null) StopCoroutine(_planCoroutine);
            _planCoroutine = null;
            _busy = false;
            _llmStatus = "已急停";
            _api?.SetLlmStatus(_llmStatus);
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null) return;
            _panelTexture = MakeTexture(new Color(0.055f, 0.075f, 0.10f, 0.96f));
            _cardTexture = MakeTexture(new Color(0.095f, 0.12f, 0.15f, 0.98f));
            _buttonTexture = MakeTexture(new Color(0.11f, 0.39f, 0.43f, 1f));
            _dangerTexture = MakeTexture(new Color(0.67f, 0.18f, 0.21f, 1f));
            try { _uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" }, 13); }
            catch { _uiFont = GUI.skin.font; }
            _panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = _panelTexture } };
            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _cardTexture },
                padding = new RectOffset(12, 12, 9, 9),
                margin = new RectOffset(0, 0, 2, 2)
            };
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.88f, 0.97f, 0.98f) } };
            _bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.86f, 0.90f, 0.92f) } };
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, normal = { textColor = new Color(0.61f, 0.71f, 0.76f) } };
            _inputStyle = new GUIStyle(GUI.skin.textArea) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.92f, 0.95f, 0.96f), background = _cardTexture } };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white, background = _buttonTexture },
                hover = { textColor = Color.white, background = _buttonTexture },
                active = { textColor = Color.white, background = _dangerTexture },
                padding = new RectOffset(8, 8, 5, 5)
            };
            _panelStyle.font = _uiFont;
            _cardStyle.font = _uiFont;
            _titleStyle.font = _uiFont;
            _bodyStyle.font = _uiFont;
            _smallStyle.font = _uiFont;
            _inputStyle.font = _uiFont;
            _buttonStyle.font = _uiFont;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            if (_panelTexture != null) Destroy(_panelTexture);
            if (_cardTexture != null) Destroy(_cardTexture);
            if (_buttonTexture != null) Destroy(_buttonTexture);
            if (_dangerTexture != null) Destroy(_dangerTexture);
            if (_uiFont != null) Destroy(_uiFont);
        }
    }
}
