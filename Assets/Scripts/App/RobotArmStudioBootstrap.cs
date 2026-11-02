using UnityEngine;

namespace RobotArmStudio
{
    /// <summary>Builds the generic workcell and connects its UI, arm fleet, and API.</summary>
    public sealed class RobotArmStudioBootstrap : MonoBehaviour
    {
        [Range(1, 4)] public int simulatedArmCount = 1;
        [Range(1024, 65535)] public int apiPort = 8765;

        private bool _built;

        private void Start()
        {
            if (_built) return;
            _built = true;
            Application.runInBackground = true;
            BuildEnvironment();

            Material printed = MakeMaterial(new Color(0.96f, 0.76f, 0.11f));
            Material motors = MakeMaterial(new Color(0.08f, 0.10f, 0.12f));
            Material accent = MakeMaterial(new Color(0.06f, 0.75f, 0.73f));

            var fleetObject = new GameObject("RobotFleet");
            fleetObject.transform.SetParent(transform, false);
            var fleet = fleetObject.AddComponent<RobotFleet>();
            fleet.Configure(simulatedArmCount, printed, motors, accent);

            BuildPayload("红色方块", new Vector3(-0.16f, 0.45f, 0.31f), new Color(0.91f, 0.29f, 0.25f));
            BuildPayload("蓝色方块", new Vector3(-0.18f, 0.45f, 0.45f), new Color(0.24f, 0.52f, 0.91f));
            BuildPayload("黄色方块", new Vector3(0.12f, 0.45f, 0.45f), new Color(0.94f, 0.69f, 0.20f));

            Material targetBorder = MakeMaterial(new Color(0.035f, 0.055f, 0.065f));
            BuildSortingTarget("红色目标框", new Vector3(-0.33f, 0.423f, 0.18f),
                MakeMaterial(new Color(1.00f, 0.12f, 0.10f)), targetBorder);
            BuildSortingTarget("蓝色目标框", new Vector3(0.30f, 0.423f, 0.38f),
                MakeMaterial(new Color(0.10f, 0.40f, 1.00f)), targetBorder);
            BuildSortingTarget("黄色目标框", new Vector3(0.33f, 0.423f, 0.18f),
                MakeMaterial(new Color(1.00f, 0.78f, 0.06f)), targetBorder);

            var api = gameObject.AddComponent<LocalRobotApi>();
            api.StartServer(fleet, apiPort, "未连接");
            var panel = gameObject.AddComponent<RobotControlPanel>();
            panel.Configure(fleet, api);
        }

        private void BuildEnvironment()
        {
            RenderSettings.ambientLight = new Color(0.38f, 0.43f, 0.48f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.12f, 0.16f, 0.20f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;

            Material floor = MakeMaterial(new Color(0.12f, 0.17f, 0.20f));
            Material table = MakeMaterial(new Color(0.24f, 0.30f, 0.33f));
            Material frame = MakeMaterial(new Color(0.16f, 0.21f, 0.24f));
            Material grid = MakeMaterial(new Color(0.20f, 0.30f, 0.32f));
            CreateCube("地面", new Vector3(0f, -0.12f, 0.20f), new Vector3(8f, 0.24f, 8f), floor, true);
            CreateCube("工作台面", new Vector3(0f, 0.36f, 0.20f), new Vector3(1.45f, 0.12f, 1.35f), table, true);
            CreateCube("工作台左腿", new Vector3(-0.62f, 0.13f, 0.20f), new Vector3(0.10f, 0.42f, 0.10f), frame, true);
            CreateCube("工作台右腿", new Vector3(0.62f, 0.13f, 0.20f), new Vector3(0.10f, 0.42f, 0.10f), frame, true);
            CreateCube("工作台后腿", new Vector3(0f, 0.13f, 0.79f), new Vector3(1.15f, 0.42f, 0.10f), frame, true);
            for (int i = 0; i < 9; i++)
                CreateCube("定位线", new Vector3(-0.60f + i * 0.15f, 0.424f, 0.20f), new Vector3(0.006f, 0.004f, 1.18f), grid, false);

            var lightObject = new GameObject("主光源");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(0.80f, 0.90f, 1f);
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            var fillObject = new GameObject("环境补光");
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.intensity = 1.6f;
            fill.range = 4f;
            fill.color = new Color(0.12f, 0.75f, 0.76f);
            fillObject.transform.position = new Vector3(-1f, 2.2f, 0.8f);

            var focus = new GameObject("相机焦点");
            focus.transform.position = new Vector3(0f, 0.55f, 0.24f);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.14f, 0.18f);
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 80f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<OrbitCameraController>().SetFocus(focus.transform);
        }

        private void BuildPayload(string displayName, Vector3 position, Color color)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = displayName;
            obj.transform.position = position;
            obj.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);
            obj.GetComponent<Renderer>().material = MakeMaterial(color);
            var payload = obj.AddComponent<ManipulablePayload>();
            payload.Configure(displayName, color, new Vector3(0.06f, 0.06f, 0.06f));
        }

        private static void BuildSortingTarget(string targetName, Vector3 position, Material color, Material border)
        {
            CreateCube(targetName + " 底板", position, new Vector3(0.15f, 0.004f, 0.15f), border, false);

            float lineY = position.y + 0.004f;
            float edge = 0.060f;
            float lineWidth = 0.010f;
            CreateCube(targetName + " 左边框", new Vector3(position.x - edge, lineY, position.z),
                new Vector3(lineWidth, 0.005f, 0.13f), color, false);
            CreateCube(targetName + " 右边框", new Vector3(position.x + edge, lineY, position.z),
                new Vector3(lineWidth, 0.005f, 0.13f), color, false);
            CreateCube(targetName + " 前边框", new Vector3(position.x, lineY, position.z - edge),
                new Vector3(0.13f, 0.005f, lineWidth), color, false);
            CreateCube(targetName + " 后边框", new Vector3(position.x, lineY, position.z + edge),
                new Vector3(0.13f, 0.005f, lineWidth), color, false);
        }

        private static void CreateCube(string objectName, Vector3 position, Vector3 size, Material material, bool collider)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = objectName;
            obj.transform.position = position;
            obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
            {
                var box = obj.GetComponent<Collider>();
                if (box != null) Destroy(box);
            }
        }

        private static Material MakeMaterial(Color color)
        {
            Shader shader = Resources.Load<Shader>("RobotArmStudioLit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("No supported material shader is available.");
            var material = new Material(shader) { color = color };
            return material;
        }
    }
}
