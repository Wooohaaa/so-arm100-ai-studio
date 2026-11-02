using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RobotArmStudio
{
    [Serializable]
    public sealed class AiLocalSettings
    {
        public string baseUrl = "https://api.openai.com/v1";
        public string model = "";
    }

    public sealed class AiPlanResult
    {
        public bool ok;
        public string error;
        public RobotActionPlan plan;
    }

    /// <summary>Small OpenAI-compatible planner client; it has no arbitrary tool or code execution.</summary>
    public static class RobotAiPlanner
    {
        private const string SettingsBaseUrlKey = "RobotArmStudio.Ai.BaseUrl";
        private const string SettingsModelKey = "RobotArmStudio.Ai.Model";

        public static AiLocalSettings LoadSettings()
        {
            return new AiLocalSettings
            {
                baseUrl = PlayerPrefs.GetString(SettingsBaseUrlKey, "https://api.openai.com/v1"),
                model = PlayerPrefs.GetString(SettingsModelKey, "")
            };
        }

        public static void SaveSettings(AiLocalSettings settings)
        {
            if (settings == null) return;
            PlayerPrefs.SetString(SettingsBaseUrlKey, settings.baseUrl ?? "");
            PlayerPrefs.SetString(SettingsModelKey, settings.model ?? "");
            PlayerPrefs.Save();
        }

        public static IEnumerator RequestPlan(
            string baseUrl,
            string model,
            string apiKey,
            string instruction,
            RobotStateSnapshot state,
            string payloadSummary,
            string conversationHistory,
            Action<AiPlanResult> finished)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            {
                finished?.Invoke(new AiPlanResult { ok = false, error = "请先填写 API Base URL 和模型 ID。" });
                yield break;
            }

            var requestBody = new ChatRequest
            {
                model = model.Trim(),
                temperature = 0.15f,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = BuildSystemPrompt() },
                    new ChatMessage { role = "user", content = BuildUserPrompt(instruction, state, payloadSummary, conversationHistory) }
                }
            };

            string endpoint = NormalizeEndpoint(baseUrl);
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri parsedEndpoint) ||
                (parsedEndpoint.Scheme != Uri.UriSchemeHttp && parsedEndpoint.Scheme != Uri.UriSchemeHttps))
            {
                finished?.Invoke(new AiPlanResult { ok = false, error = "Base URL 必须是有效的 HTTP 或 HTTPS 地址。" });
                yield break;
            }

            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestBody));
            using (var request = new UnityWebRequest(endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 60;
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string detail = request.downloadHandler != null ? request.downloadHandler.text : "";
                    if (detail.Length > 320) detail = detail.Substring(0, 320);
                    finished?.Invoke(new AiPlanResult
                    {
                        ok = false,
                        error = $"模型请求失败：{request.error}" + (string.IsNullOrEmpty(detail) ? "" : " · " + detail)
                    });
                    yield break;
                }

                ChatResponse response;
                try { response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text); }
                catch (Exception e)
                {
                    finished?.Invoke(new AiPlanResult { ok = false, error = "无法解析模型服务响应：" + e.Message });
                    yield break;
                }

                string content = response != null && response.choices != null && response.choices.Length > 0 &&
                                 response.choices[0] != null && response.choices[0].message != null
                    ? response.choices[0].message.content
                    : null;
                if (!TryParsePlan(content, state != null ? state.armCount : 0, out RobotActionPlan plan, out string error))
                {
                    finished?.Invoke(new AiPlanResult { ok = false, error = error });
                    yield break;
                }
                finished?.Invoke(new AiPlanResult { ok = true, plan = plan });
            }
        }

        private static string NormalizeEndpoint(string baseUrl)
        {
            string url = (baseUrl ?? "").Trim().TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
            return url + "/chat/completions";
        }

        private static string BuildSystemPrompt()
        {
            return "你是一个仅控制 SO-100 仿真机械臂的运动规划器。根据当前状态，把用户请求转换为有限动作序列。" +
                   "只能输出一个 JSON 对象，不要 Markdown、代码块或额外文本，格式：" +
                   "{\"summary\":\"简短中文说明\",\"actions\":[{" +
                   "\"type\":\"move|joints|gripper|home|wait|stop\",\"arm\":0," +
                   "\"target\":[x,y,z],\"open\":true,\"waitSeconds\":1.0}]}。" +
                   "move 使用米制世界坐标，X 左右、Y 向上、Z 向前；工作空间为 X[-0.48,0.48]、" +
                   "Y[0.43,0.78]、Z[-0.35,0.55]。move 必须提供 target 三个有限数值。" +
                   "joints 的 target 必须按 [shoulder_pan, shoulder_lift, elbow_flex, wrist_flex, wrist_roll] " +
                   "顺序提供 5 个角度（度），夹爪仍使用独立 gripper 动作；机械臂会检查每个关节限位。" +
                   "gripper 的 open=true 是松开、" +
                   "false 是闭合；home 回零；waitSeconds 为 0 到 10；stop 立即停止。最多 8 个动作。" +
                   "不要声称已完成尚未执行的动作。如果用户请求超出能力或工作空间，返回空 actions 并在 summary 说明。";
        }

        private static string BuildUserPrompt(string instruction, RobotStateSnapshot state, string payloadSummary, string conversationHistory)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(conversationHistory))
                builder.Append("最近对话背景（只作为任务上下文，仍需依据当前状态规划）：\n")
                    .Append(conversationHistory).Append("\n\n");
            builder.Append("用户指令：").Append(instruction).Append("\n\n当前状态：");
            builder.Append(state != null ? JsonUtility.ToJson(state) : "未知");
            builder.Append("\n\n可抓取物体：\n").Append(payloadSummary ?? "未知");
            builder.Append("\n请给出需要执行的动作序列。优先使用多个短距离 move 逐步完成任务；每个动作都必须能在仿真安全范围内执行。");
            return builder.ToString();
        }

        private static bool TryParsePlan(string content, int armCount, out RobotActionPlan plan, out string error)
        {
            plan = null;
            error = "模型没有返回可读取的动作 JSON。";
            if (string.IsNullOrWhiteSpace(content)) return false;

            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start < 0 || end <= start) return false;

            try { plan = JsonUtility.FromJson<RobotActionPlan>(content.Substring(start, end - start + 1)); }
            catch (Exception e)
            {
                error = "模型返回的 JSON 格式错误：" + e.Message;
                return false;
            }

            if (plan == null || plan.actions == null)
            {
                error = "模型响应缺少 actions 数组。";
                return false;
            }
            if (plan.actions.Length > RobotSafety.MaxActionsPerPlan)
            {
                error = $"动作数量超过限制（最多 {RobotSafety.MaxActionsPerPlan} 个）。";
                return false;
            }

            for (int i = 0; i < plan.actions.Length; i++)
            {
                RobotAction action = plan.actions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.type))
                {
                    error = $"第 {i + 1} 个动作缺少类型。";
                    return false;
                }

                action.type = action.type.Trim().ToLowerInvariant();
                if (action.type == "stop") continue;
                if (action.arm < 0 || action.arm >= armCount)
                {
                    error = $"第 {i + 1} 个动作引用了不存在的机械臂。";
                    return false;
                }

                if (action.type == "move")
                {
                    string targetError = null;
                    if (!RobotSafety.TryReadTarget(action.target, out Vector3 target) ||
                        !RobotSafety.TryValidateTarget(target, out targetError))
                    {
                        error = targetError ?? "移动动作的 target 格式错误。";
                        return false;
                    }
                }
                else if (action.type == "joints")
                {
                    if (action.target == null || action.target.Length != 5)
                    {
                        error = "关节动作 target 必须是按 SO-100 关节顺序排列的 5 个角度。";
                        return false;
                    }
                    for (int joint = 0; joint < action.target.Length; joint++)
                    {
                        if (!RobotSafety.IsFinite(action.target[joint]))
                        {
                            error = $"第 {i + 1} 个动作的关节角度包含无效数值。";
                            return false;
                        }
                    }
                }
                else if (action.type == "wait")
                {
                    if (!RobotSafety.IsFinite(action.waitSeconds) || action.waitSeconds < 0f || action.waitSeconds > 10f)
                    {
                        error = "waitSeconds 必须在 0 到 10 秒之间。";
                        return false;
                    }
                }
                else if (action.type != "gripper" && action.type != "home")
                {
                    error = $"第 {i + 1} 个动作类型不受支持：{action.type}。";
                    return false;
                }
            }

            error = null;
            return true;
        }

        [Serializable] private sealed class ChatMessage { public string role; public string content; }
        [Serializable] private sealed class ChatRequest
        {
            public string model;
            public float temperature;
            public ChatMessage[] messages;
        }
        [Serializable] private sealed class ChatChoice { public ChatMessage message; }
        [Serializable] private sealed class ChatResponse { public ChatChoice[] choices; }
    }
}
