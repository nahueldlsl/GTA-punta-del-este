using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;
using GtaPunta.Atmosphere;
using GtaPunta.Core;
using GtaPunta.Wanted;

namespace GtaPunta.UI
{
    /// <summary>
    /// Modern GTA Coastal HUD & Game Shell (Title, Playing HUD, Paused/Settings, Interactive Map, Busted).
    /// Built purely on uGUI + TextMeshPro + Palette.cs (zero default Unity gray UI, zero OnGUI).
    /// Mounted on a Screen Space - Camera Canvas paired with an URP Overlay UI Camera so both
    /// runtime builds and Pipeline captures display every HUD element crisply without 3D clipping.
    /// </summary>
    public class GtaHudController : MonoBehaviour
    {
        // Terrain mapping constants matching Peninsula_Heightmap / Minimap_PuntaDelEste.png
        private const float MapMinX = -1100f;
        private const float MapMinZ = -1200f;
        private const float MapSize = 2400f;
        private const float MinimapTexSize = 960f; // Zoomed scale inside 240x192 viewport

        public Camera uiOverlayCamera;

        private Canvas _canvas;

        private void Start()
        {
            if (_canvas == null)
                BuildUI(uiOverlayCamera);
        }
        private Texture2D _mapTexture;
        private Sprite _whiteSprite;

        // Containers by GameState
        private GameObject _titlePanel;
        private GameObject _hudRoot;
        private GameObject _pausePanel;
        private GameObject _mapPanel;
        private GameObject _bustedPanel;

        // Title animation
        private TextMeshProUGUI _titlePromptText;

        // Minimap elements (GTA V-style rotating radar + orbiting N compass + status bars)
        private RectTransform _minimapRotatePivot;
        private RectTransform _minimapImageRect;
        private RectTransform _minimapPlayerArrow;
        private RectTransform _minimapBlipsParent;
        private RectTransform _compassNorthRect;
        private Image _staminaBarFill;
        private float _smoothMapYaw;
        private TextMeshProUGUI _districtText;
        private readonly List<RectTransform> _landmarkMiniBlips = new List<RectTransform>();
        private readonly List<Image> _policeMiniBlips = new List<Image>();

        // Smooth transition fade veil
        private Image _fadeVeil;
        private float _fadeAlpha;

        // Full map player marker
        private RectTransform _fullMapPlayerDot;

        // Top-right HUD
        private readonly Image[] _starIcons = new Image[5];
        private TextMeshProUGUI _wantedSubText;
        private GameObject _bustedBarRoot;
        private Image _bustedBarFill;
        private TextMeshProUGUI _timeText;
        private TextMeshProUGUI _discoveryText;
        private TextMeshProUGUI _trialText;

        // Speedometer (bottom-right)
        private GameObject _speedoRoot;
        private TextMeshProUGUI _speedValText;
        private TextMeshProUGUI _gearText;
        private TextMeshProUGUI _vehicleNameText;
        private TextMeshProUGUI _vehicleBadgesText;
        private Image _rpmFill;

        // Contextual interaction & toast banners
        private GameObject _interactPromptRoot;
        private TextMeshProUGUI _interactPromptText;
        private GameObject _toastRoot;
        private Image _toastAccentBar;
        private TextMeshProUGUI _toastText;
        private float _toastTimer;

        // Settings sliders
        private Slider _musicSlider;
        private Slider _sfxSlider;

        public void BuildUI(Camera uiOverlayCamera)
        {
            EnsureEventSystem();
            _whiteSprite = CreateSolidSprite();
            _mapTexture = Resources.Load<Texture2D>("UI/Minimap_PuntaDelEste");

            // Setup Canvas in ScreenSpaceOverlay so UI renders at crisp native Full HD in both standalone builds & URP captures!
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;

            var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            gameObject.layer = LayerMask.NameToLayer("UI");

            BuildPlayingHud();
            BuildTitleScreen();
            BuildPauseScreen();
            BuildFullMapScreen();
            BuildBustedScreen();

            // Smooth full-screen black transition veil (non-blocking)
            var fadeGo = CreatePanel("TransitionFadeVeil", transform, new Color(0.03f, 0.05f, 0.08f, 0f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _fadeVeil = fadeGo.GetComponent<Image>();
            _fadeVeil.raycastTarget = false;

            SetLayerRecursive(transform, LayerMask.NameToLayer("UI"));
            ApplyState(GameState.Title);
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static Sprite CreateSolidSprite()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var cols = new Color[16];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            tex.SetPixels(cols);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateArrowSprite()
        {
            int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float nx = (x - s * 0.5f) / (s * 0.5f);
                    float ny = (y - s * 0.5f) / (s * 0.5f);
                    // Crisp GTA navigation chevron pointing UP (+Y)
                    bool inside = ny <= 0.85f && ny >= -0.75f && Mathf.Abs(nx) <= (0.85f - ny) * 0.55f && !(ny < -0.25f && Mathf.Abs(nx) < (-ny - 0.25f) * 1.15f);
                    tex.SetPixel(x, y, inside ? Color.white : new Color(0, 0, 0, 0));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateStarSprite()
        {
            int s = 48;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - s * 0.5f) / (s * 0.48f);
                    float dy = (y - s * 0.5f) / (s * 0.48f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) - Mathf.PI * 0.5f;
                    float sector = Mathf.Repeat(ang, Mathf.PI * 0.4f) - Mathf.PI * 0.2f;
                    float maxR = 0.42f / Mathf.Max(0.28f, Mathf.Cos(sector) * 0.42f + Mathf.Abs(Mathf.Sin(sector)) * 0.92f);
                    // 5-pointed sharp star contour
                    float starEdge = Mathf.Lerp(0.42f, 0.98f, Mathf.Pow(Mathf.Cos(sector * 2.5f), 2f));
                    tex.SetPixel(x, y, r <= starEdge ? Color.white : new Color(0, 0, 0, 0));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        }

        private static void SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }

        private GameObject CreatePanel(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = _whiteSprite;
            img.color = color;
            return go;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 boxSize, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = boxSize;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            return tmp;
        }

        private Button CreateStyledButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, Color bgColor, Color textColor, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreatePanel(name, parent, bgColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cols.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            btn.colors = cols;
            btn.onClick.AddListener(onClick);

            // Thin gold left accent bar
            CreatePanel("Accent", go.transform, Palette.HudGoldAccent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, 0f));

            CreateText("Label", go.transform, label, 20f, textColor, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, FontStyles.Bold);
            return btn;
        }

        private void BuildPlayingHud()
        {
            _hudRoot = new GameObject("HUD_Playing");
            _hudRoot.transform.SetParent(transform, false);
            var hrt = _hudRoot.AddComponent<RectTransform>();
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
            hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;

            // --- 1. BOTTOM-LEFT GTA V-STYLE ROTATING MINIMAP ---
            var miniFrame = CreatePanel("MinimapFrame", _hudRoot.transform, Palette.HudPanelBg,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 76f), new Vector2(268f, 198f));
            var border = miniFrame.AddComponent<Outline>();
            border.effectColor = Palette.HudPanelBorder;
            border.effectDistance = new Vector2(2.5f, -2.5f);

            var maskGo = new GameObject("MinimapViewport");
            maskGo.transform.SetParent(miniFrame.transform, false);
            var mrt = maskGo.AddComponent<RectTransform>();
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
            mrt.offsetMin = new Vector2(6f, 6f); mrt.offsetMax = new Vector2(-6f, -6f);
            maskGo.AddComponent<RectMask2D>();

            // Rotation pivot centered inside minimap viewport so map smoothly rotates around player heading!
            var rotGo = new GameObject("MinimapRotatePivot");
            rotGo.transform.SetParent(maskGo.transform, false);
            _minimapRotatePivot = rotGo.AddComponent<RectTransform>();
            _minimapRotatePivot.anchorMin = new Vector2(0.5f, 0.5f);
            _minimapRotatePivot.anchorMax = new Vector2(0.5f, 0.5f);
            _minimapRotatePivot.sizeDelta = Vector2.zero;

            var mapImgGo = new GameObject("MinimapTexture");
            mapImgGo.transform.SetParent(rotGo.transform, false);
            _minimapImageRect = mapImgGo.AddComponent<RectTransform>();
            _minimapImageRect.anchorMin = new Vector2(0.5f, 0.5f);
            _minimapImageRect.anchorMax = new Vector2(0.5f, 0.5f);
            _minimapImageRect.pivot = new Vector2(0.5f, 0.5f);
            _minimapImageRect.sizeDelta = new Vector2(MinimapTexSize, MinimapTexSize);
            var rawMap = mapImgGo.AddComponent<RawImage>();
            rawMap.texture = _mapTexture;
            rawMap.color = Color.white;

            // Dynamic Blips Parent attached to map texture so blips track & rotate cleanly!
            var blipsGo = new GameObject("BlipsLayer");
            blipsGo.transform.SetParent(mapImgGo.transform, false);
            _minimapBlipsParent = blipsGo.AddComponent<RectTransform>();
            _minimapBlipsParent.anchorMin = Vector2.zero; _minimapBlipsParent.anchorMax = Vector2.one;
            _minimapBlipsParent.offsetMin = Vector2.zero; _minimapBlipsParent.offsetMax = Vector2.zero;

            // Create 4 reusable police blip markers
            for (int i = 0; i < 4; i++)
            {
                var pBlip = CreatePanel("PoliceBlip_" + i, _minimapBlipsParent, Palette.HudWantedAlert,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f));
                var pImg = pBlip.GetComponent<Image>();
                pBlip.SetActive(false);
                _policeMiniBlips.Add(pImg);
            }

            // Center Gold Player Arrow (always points UP while world rotates beneath it, classic GTA V!)
            var arrowGo = new GameObject("PlayerArrow");
            arrowGo.transform.SetParent(maskGo.transform, false);
            _minimapPlayerArrow = arrowGo.AddComponent<RectTransform>();
            _minimapPlayerArrow.anchorMin = new Vector2(0.5f, 0.5f);
            _minimapPlayerArrow.anchorMax = new Vector2(0.5f, 0.5f);
            _minimapPlayerArrow.sizeDelta = new Vector2(20f, 20f);
            var aImg = arrowGo.AddComponent<Image>();
            aImg.sprite = CreateArrowSprite();
            aImg.color = Palette.HudGoldAccent;

            // Orbiting North compass badge pinned along minimap bezel
            var nPill = CreatePanel("CompassNBadge", miniFrame.transform, new Color(0.05f, 0.09f, 0.14f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 94f), new Vector2(22f, 20f));
            _compassNorthRect = nPill.GetComponent<RectTransform>();
            CreateText("CompassN", nPill.transform, "N", 13f, Palette.HudCyanAccent, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, FontStyles.Bold);

            // GTA V-style compact Health (green) & Sprint Stamina (cyan) bar strip directly under minimap!
            var barsBg = CreatePanel("StatusBarsBg", _hudRoot.transform, Palette.HudPanelBg,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 62f), new Vector2(268f, 10f));
            CreatePanel("HealthBar", barsBg.transform, Palette.HudSuccess,
                new Vector2(0f, 0.15f), new Vector2(0.49f, 0.85f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            var stamBg = CreatePanel("StaminaBg", barsBg.transform, new Color(0.14f, 0.20f, 0.28f, 0.85f),
                new Vector2(0.51f, 0.15f), new Vector2(1f, 0.85f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            var stamFill = CreatePanel("StaminaFill", stamBg.transform, Palette.HudCyanAccent,
                Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            _staminaBarFill = stamFill.GetComponent<Image>();
            _staminaBarFill.type = Image.Type.Filled;
            _staminaBarFill.fillMethod = Image.FillMethod.Horizontal;

            // District / Street Name Banner directly below status bars
            var distPanel = CreatePanel("DistrictBanner", _hudRoot.transform, Palette.HudPanelBg,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 24f), new Vector2(268f, 34f));
            CreatePanel("DistAccent", distPanel.transform, Palette.HudCyanAccent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, 0f));
            _districtText = CreateText("DistrictText", distPanel.transform, "PENÍNSULA — PUNTA DEL ESTE", 13.5f, Palette.HudTextLight,
                TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-16f, 0f), FontStyles.Bold);

            // --- 2. TOP-RIGHT WANTED STARS & INFO CLUSTER ---
            var starSprite = CreateStarSprite();
            var topRight = CreatePanel("TopRightCluster", _hudRoot.transform, Palette.HudPanelBg,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(310f, 126f));
            CreatePanel("TopAccent", topRight.transform, Palette.HudGoldAccent, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(4f, 0f));

            // 5 Star Slots
            for (int i = 0; i < 5; i++)
            {
                var sGo = new GameObject("Star_" + i);
                sGo.transform.SetParent(topRight.transform, false);
                var srt = sGo.AddComponent<RectTransform>();
                srt.anchorMin = new Vector2(1f, 1f); srt.anchorMax = new Vector2(1f, 1f);
                srt.pivot = new Vector2(1f, 1f);
                srt.anchoredPosition = new Vector2(-16f - (4 - i) * 38f, -12f);
                srt.sizeDelta = new Vector2(32f, 32f);
                var simg = sGo.AddComponent<Image>();
                simg.sprite = starSprite;
                simg.color = new Color(0.25f, 0.32f, 0.42f, 0.45f);
                _starIcons[i] = simg;
            }

            _wantedSubText = CreateText("WantedStatus", topRight.transform, "SIN BÚSQUEDA POLICIAL", 12.5f, Palette.HudTextMuted,
                TextAlignmentOptions.MidlineRight, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -54f), new Vector2(-16f, 20f), FontStyles.Bold);

            _timeText = CreateText("TimeOfDayText", topRight.transform, "Tarde Soleada · 16:30 [T]", 14f, Palette.HudCyanAccent,
                TextAlignmentOptions.MidlineRight, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -78f), new Vector2(-16f, 22f), FontStyles.Bold);

            _discoveryText = CreateText("DiscoveryText", topRight.transform, "LUGARES ICÓNICOS: 0 / 6", 13.5f, Palette.HudGoldAccent,
                TextAlignmentOptions.MidlineRight, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -104f), new Vector2(-16f, 22f), FontStyles.Bold);

            // Busted warning bar (hidden unless police pinning player)
            _bustedBarRoot = CreatePanel("BustedBarRoot", _hudRoot.transform, Palette.HudPanelBg,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -158f), new Vector2(310f, 28f));
            var bFill = CreatePanel("BustedFill", _bustedBarRoot.transform, Palette.HudWantedAlert,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            _bustedBarFill = bFill.GetComponent<Image>();
            _bustedBarFill.type = Image.Type.Filled;
            _bustedBarFill.fillMethod = Image.FillMethod.Horizontal;
            CreateText("BustedLbl", _bustedBarRoot.transform, "POLICÍA CERCANDO — ¡ACELERÁ!", 12f, Palette.HudTextLight,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, FontStyles.Bold);
            _bustedBarRoot.SetActive(false);

            // Rambla Circuit Timer Banner (top-center, only when active)
            _trialText = CreateText("TrialTimer", _hudRoot.transform, "", 20f, Palette.HudGoldAccent,
                TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(460f, 36f), FontStyles.Bold);

            // --- 3. BOTTOM-RIGHT MODERN SPEEDOMETER (Vehicle Only) ---
            _speedoRoot = CreatePanel("SpeedometerPanel", _hudRoot.transform, Palette.HudPanelBg,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 24f), new Vector2(280f, 118f));
            CreatePanel("SpeedoAccent", _speedoRoot.transform, Palette.HudCyanAccent, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(4f, 0f));

            _speedValText = CreateText("SpeedVal", _speedoRoot.transform, "0", 52f, Palette.HudTextLight,
                TextAlignmentOptions.MidlineRight, new Vector2(0f, 0.42f), new Vector2(0.62f, 1f), Vector2.zero, Vector2.zero, FontStyles.Bold);
            CreateText("SpeedUnit", _speedoRoot.transform, "KM/H", 15f, Palette.HudCyanAccent,
                TextAlignmentOptions.BottomLeft, new Vector2(0.65f, 0.46f), new Vector2(0.95f, 0.88f), Vector2.zero, Vector2.zero, FontStyles.Bold);

            // Gear Pill
            var gearBg = CreatePanel("GearBox", _speedoRoot.transform, new Color(0.14f, 0.24f, 0.36f, 0.9f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(42f, 42f));
            _gearText = CreateText("GearTxt", gearBg.transform, "N", 24f, Palette.HudGoldAccent, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, FontStyles.Bold);

            // RPM Bar
            var rpmBg = CreatePanel("RpmBg", _speedoRoot.transform, new Color(0.16f, 0.22f, 0.30f, 0.9f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(-2f, 38f), new Vector2(-28f, 10f));
            var rpmF = CreatePanel("RpmFill", rpmBg.transform, Palette.HudCyanAccent, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            _rpmFill = rpmF.GetComponent<Image>();
            _rpmFill.type = Image.Type.Filled;
            _rpmFill.fillMethod = Image.FillMethod.Horizontal;

            _vehicleNameText = CreateText("VehicleName", _speedoRoot.transform, "PUNTA CABRIO GT", 13.5f, Palette.HudTextLight,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(0.62f, 0f), new Vector2(14f, 19f), new Vector2(0f, 22f), FontStyles.Bold);
            _vehicleBadgesText = CreateText("VehicleBadges", _speedoRoot.transform, "FM ON", 12f, Palette.HudGoldAccent,
                TextAlignmentOptions.MidlineRight, new Vector2(0.6f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 19f), new Vector2(0f, 22f), FontStyles.Bold);
            _speedoRoot.SetActive(false);

            // --- 4. CONTEXTUAL INTERACT PROMPT (Bottom-Center) ---
            _interactPromptRoot = CreatePanel("InteractPrompt", _hudRoot.transform, Palette.HudPanelBg,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(360f, 44f));
            CreatePanel("PromptBar", _interactPromptRoot.transform, Palette.HudGoldAccent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, 0f));
            _interactPromptText = CreateText("PromptTxt", _interactPromptRoot.transform, "F — SUBIR AL VEHÍCULO", 17f, Palette.HudTextLight,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, FontStyles.Bold);
            _interactPromptRoot.SetActive(false);

            // --- 5. TOAST NOTIFICATION BANNER (Top-Center) ---
            _toastRoot = CreatePanel("ToastBanner", _hudRoot.transform, Palette.HudPanelBg,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(640f, 46f));
            var accentObj = CreatePanel("ToastAccent", _toastRoot.transform, Palette.HudGoldAccent,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 3.5f));
            _toastAccentBar = accentObj.GetComponent<Image>();
            _toastText = CreateText("ToastTxt", _toastRoot.transform, "", 16.5f, Palette.HudTextLight,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, FontStyles.Bold);
            _toastRoot.SetActive(false);

            // --- 6. SUBTLE CONTROLS HINT STRIP (Top-Left) ---
            var ctrlBar = CreatePanel("ControlsStrip", _hudRoot.transform, new Color(Palette.HudPanelBg.r, Palette.HudPanelBg.g, Palette.HudPanelBg.b, 0.65f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(455f, 34f));
            CreateText("ControlsHintTxt", ctrlBar.transform, "WASD Mover · F Auto · T Hora · M Mapa · K Circuito · V Radio · ESC Pausa", 12.5f,
                Palette.HudTextMuted, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, FontStyles.Bold);
        }

        private void BuildTitleScreen()
        {
            _titlePanel = new GameObject("TitleScreen");
            _titlePanel.transform.SetParent(transform, false);
            var rt = _titlePanel.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // Compact sleek top header & bottom CTA bar so Los Dedos (La Mano) & Playa Brava stay 100% unobstructed in center!
            CreatePanel("TitleTopScrim", _titlePanel.transform, new Color(Palette.ScrimDark.r, Palette.ScrimDark.g, Palette.ScrimDark.b, 0.72f),
                new Vector2(0f, 0.72f), new Vector2(1f, 0.96f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            CreatePanel("TitleTopAccent", _titlePanel.transform, Palette.HudGoldAccent,
                new Vector2(0.18f, 0.72f), new Vector2(0.82f, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 3f));

            CreateText("BrandSub", _titlePanel.transform, "REPÚBLICA ORIENTAL DEL URUGUAY · MALDONADO", 18f, Palette.HudCyanAccent,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.91f), new Vector2(0.5f, 0.91f), Vector2.zero, new Vector2(820f, 30f), FontStyles.Bold);

            CreateText("MainTitle", _titlePanel.transform, "GTA: PUNTA DEL ESTE", 76f, Palette.HudGoldAccent,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.835f), new Vector2(0.5f, 0.835f), Vector2.zero, new Vector2(1100f, 94f), FontStyles.Bold);

            CreateText("Tagline", _titlePanel.transform, "PENÍNSULA · LOS DEDOS · PLAYA BRAVA · PLAYA MANSA · PUERTO · EL FARO · AV. GORLERO", 16f, Palette.HudTextLight,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.755f), new Vector2(0.5f, 0.755f), Vector2.zero, new Vector2(1060f, 28f), FontStyles.Bold);

            CreatePanel("TitleBotScrim", _titlePanel.transform, new Color(Palette.ScrimDark.r, Palette.ScrimDark.g, Palette.ScrimDark.b, 0.72f),
                new Vector2(0.28f, 0.04f), new Vector2(0.72f, 0.24f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var playBtn = CreateStyledButton("PlayBtn", _titlePanel.transform, "EXPLORAR PUNTA DEL ESTE", Vector2.zero, new Vector2(360f, 56f),
                new Color(0.08f, 0.24f, 0.40f, 0.96f), Palette.HudTextLight, () => GameManager.Instance?.StartPlaying());
            var pbrt = playBtn.GetComponent<RectTransform>();
            pbrt.anchorMin = new Vector2(0.5f, 0.165f);
            pbrt.anchorMax = new Vector2(0.5f, 0.165f);
            pbrt.anchoredPosition = Vector2.zero;

            _titlePromptText = CreateText("PressEnterHint", _titlePanel.transform, "PRESIONÁ ENTER O ESPACIO PARA COMENZAR", 15f, Palette.HudTextLight,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), Vector2.zero, new Vector2(680f, 28f), FontStyles.Bold);
        }

        private void BuildPauseScreen()
        {
            _pausePanel = CreatePanel("PauseOverlay", transform, Palette.ScrimDark, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var card = CreatePanel("PauseCard", _pausePanel.transform, Palette.HudPanelBg,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 540f));
            var outl = card.AddComponent<Outline>();
            outl.effectColor = Palette.HudPanelBorder;
            outl.effectDistance = new Vector2(2f, -2f);

            CreateText("PauseTitle", card.transform, "PAUSA — PUNTA DEL ESTE", 30f, Palette.HudGoldAccent,
                TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(540f, 44f), FontStyles.Bold);

            // Music & SFX Sliders
            _musicSlider = CreateAudioSlider(card.transform, "VOLUMEN MÚSICA / RADIO", new Vector2(0f, 130f), 0.60f, v =>
            {
                if (AudioManager.Instance != null) AudioManager.Instance.musicGain = v;
            });
            _sfxSlider = CreateAudioSlider(card.transform, "VOLUMEN EFECTOS Y MAR", new Vector2(0f, 65f), 0.85f, v =>
            {
                if (AudioManager.Instance != null) AudioManager.Instance.sfxGain = v;
            });

            // Controls Legend
            CreateText("ControlsLegend", card.transform,
                "WASD / Flechas: Caminar / Conducir   |   Shift: Correr   |   Espacio: Saltar / Freno de Mano\n" +
                "F / E: Subir o Bajar de cualquier auto   |   H: Bocina   |   Q: Sirena (Patrullero)\n" +
                "T: Cambiar Hora (Tarde / Atardecer / Noche)   |   M: Mapa y Viaje Rápido   |   K: Circuito Rambla   |   R: Enderezar Auto",
                13.5f, Palette.HudTextMuted, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -15f), new Vector2(540f, 82f));

            CreateStyledButton("ResumeBtn", card.transform, "REANUDAR", new Vector2(0f, -105f), new Vector2(280f, 46f),
                new Color(0.10f, 0.30f, 0.44f, 0.95f), Palette.HudTextLight, () => GameManager.Instance?.SetState(GameState.Playing));
            CreateStyledButton("RestartBtn", card.transform, "REINICIAR SESIÓN", new Vector2(0f, -162f), new Vector2(280f, 44f),
                new Color(0.16f, 0.22f, 0.30f, 0.95f), Palette.HudTextLight, () => GameManager.Instance?.RestartSession());
            CreateStyledButton("QuitBtn", card.transform, "SALIR DEL JUEGO", new Vector2(0f, -218f), new Vector2(280f, 44f),
                new Color(0.42f, 0.14f, 0.16f, 0.95f), Palette.HudTextLight, () =>
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                });
        }

        private Slider CreateAudioSlider(Transform parent, string label, Vector2 pos, float initial, UnityEngine.Events.UnityAction<float> onVal)
        {
            CreateText(label + "_Txt", parent, label, 14f, Palette.HudTextLight, TextAlignmentOptions.MidlineLeft,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos + new Vector2(-120f, 20f), new Vector2(260f, 24f), FontStyles.Bold);

            var root = CreatePanel(label + "_Slider", parent, new Color(0.14f, 0.20f, 0.28f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(460f, 14f));
            var slider = root.AddComponent<Slider>();
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = initial;

            var fillArea = new GameObject("FillArea"); fillArea.transform.SetParent(root.transform, false);
            var fart = fillArea.AddComponent<RectTransform>(); fart.anchorMin = Vector2.zero; fart.anchorMax = Vector2.one; fart.offsetMin = Vector2.zero; fart.offsetMax = Vector2.zero;
            var fill = CreatePanel("Fill", fillArea.transform, Palette.HudCyanAccent, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            slider.fillRect = fill.GetComponent<RectTransform>();

            var handleArea = new GameObject("HandleArea"); handleArea.transform.SetParent(root.transform, false);
            var hart = handleArea.AddComponent<RectTransform>(); hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one; hart.offsetMin = Vector2.zero; hart.offsetMax = Vector2.zero;
            var handle = CreatePanel("Handle", handleArea.transform, Palette.HudGoldAccent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 24f));
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();

            slider.onValueChanged.AddListener(onVal);
            return slider;
        }

        private void BuildFullMapScreen()
        {
            _mapPanel = CreatePanel("FullMapOverlay", transform, Palette.ScrimDark, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var frame = CreatePanel("MapContainer", _mapPanel.transform, Palette.HudPanelBg,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1020f, 680f));
            var outl = frame.AddComponent<Outline>();
            outl.effectColor = Palette.HudPanelBorder;
            outl.effectDistance = new Vector2(2f, -2f);

            CreateText("MapTitle", frame.transform, "MAPA DE LA PENÍNSULA — PUNTA DEL ESTE (VIAJE RÁPIDO)", 24f, Palette.HudGoldAccent,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -34f), new Vector2(-40f, 40f), FontStyles.Bold);

            // Large 560x560 map on the left
            var mapGo = new GameObject("FullMapImg");
            mapGo.transform.SetParent(frame.transform, false);
            var mrt = mapGo.AddComponent<RectTransform>();
            mrt.anchorMin = new Vector2(0f, 0.5f); mrt.anchorMax = new Vector2(0f, 0.5f);
            mrt.pivot = new Vector2(0f, 0.5f);
            mrt.anchoredPosition = new Vector2(28f, -18f);
            mrt.sizeDelta = new Vector2(560f, 560f);
            var raw = mapGo.AddComponent<RawImage>();
            raw.texture = _mapTexture;

            var pDot = CreatePanel("FullMapPlayerDot", mapGo.transform, Palette.HudGoldAccent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f));
            _fullMapPlayerDot = pDot.GetComponent<RectTransform>();

            // Fast-travel landmark buttons on the right side
            var landmarks = new (string id, string label)[]
            {
                ("los_dedos",          "1. LOS DEDOS (PLAYA BRAVA)"),
                ("puerto",             "2. PUERTO Y PLAYA MANSA"),
                ("el_faro",            "3. EL FARO DE PUNTA DEL ESTE"),
                ("plaza_artigas",      "4. AV. GORLERO Y PLAZA ARTIGAS"),
                ("iglesia_candelaria", "5. IGLESIA DE LA CANDELARIA"),
                ("punta_salinas",      "6. PUNTA DE LAS SALINAS"),
            };

            for (int i = 0; i < landmarks.Length; i++)
            {
                string lid = landmarks[i].id;
                CreateStyledButton("FT_" + lid, frame.transform, landmarks[i].label,
                    new Vector2(304f, 185f - i * 68f), new Vector2(360f, 52f),
                    new Color(0.09f, 0.22f, 0.35f, 0.95f), Palette.HudTextLight,
                    () => GameManager.Instance?.TeleportToLandmark(lid));
            }

            CreateStyledButton("CloseMapBtn", frame.transform, "CERRAR MAPA [M]", new Vector2(304f, -245f), new Vector2(360f, 48f),
                new Color(0.24f, 0.28f, 0.34f, 0.95f), Palette.HudGoldAccent,
                () => GameManager.Instance?.SetState(GameState.Playing));
        }

        private void BuildBustedScreen()
        {
            _bustedPanel = CreatePanel("BustedOverlay", transform, new Color(0.14f, 0.04f, 0.06f, 0.72f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            CreateText("BustedBig", _bustedPanel.transform, "ATRAPADO", 88f, Palette.HudWantedAlert,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.54f), new Vector2(0.5f, 0.54f), Vector2.zero, new Vector2(800f, 110f), FontStyles.Bold);
            CreateText("BustedSub", _bustedPanel.transform, "LA POLICÍA DE MALDONADO TE DETUVO · REAPARECIENDO EN AV. GORLERO...", 20f, Palette.HudTextLight,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.44f), new Vector2(0.5f, 0.44f), Vector2.zero, new Vector2(900f, 40f), FontStyles.Bold);
        }

        public void ApplyState(GameState state)
        {
            if (_titlePanel != null) _titlePanel.SetActive(state == GameState.Title);
            if (_hudRoot != null) _hudRoot.SetActive(state == GameState.Playing);
            if (_pausePanel != null) _pausePanel.SetActive(state == GameState.Paused);
            if (_mapPanel != null) _mapPanel.SetActive(state == GameState.MapOpen);
            if (_bustedPanel != null) _bustedPanel.SetActive(state == GameState.Busted);

            if (state == GameState.Playing)
            {
                EnsureLandmarkMinimapPins();
                TriggerTransitionFade(0.55f);
            }
        }

        public void TriggerTransitionFade(float peakAlpha = 0.65f)
        {
            _fadeAlpha = Mathf.Max(_fadeAlpha, peakAlpha);
        }

        public void ShowToast(string message, Color accentColor, float duration = 3.2f)
        {
            if (_toastRoot == null || _toastText == null) return;
            _toastText.text = message;
            if (_toastAccentBar != null) _toastAccentBar.color = accentColor;
            _toastRoot.SetActive(true);
            _toastTimer = duration;
        }

        private void EnsureLandmarkMinimapPins()
        {
            if (_landmarkMiniBlips.Count > 0 || _minimapBlipsParent == null) return;
            var rn = GameManager.Instance?.roadNetwork;
            if (rn == null) return;

            foreach (var lm in rn.landmarks)
            {
                var pin = CreatePanel("Pin_" + lm.id, _minimapBlipsParent, Palette.HudCyanAccent,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    WorldToMapPixel(lm.position, MinimapTexSize), new Vector2(10f, 10f));
                _landmarkMiniBlips.Add(pin.GetComponent<RectTransform>());
            }
        }

        private static Vector2 WorldToMapPixel(Vector3 worldPos, float mapPixelSize)
        {
            float u = Mathf.Clamp01((worldPos.x - MapMinX) / MapSize);
            float v = Mathf.Clamp01((worldPos.z - MapMinZ) / MapSize);
            return new Vector2((u - 0.5f) * mapPixelSize, (v - 0.5f) * mapPixelSize);
        }

        private void Update()
        {
            // Smooth transition fade veil decay (always ticks cleanly even when paused!)
            if (_fadeVeil != null && _fadeAlpha > 0.001f)
            {
                _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, 0f, Time.unscaledDeltaTime * 2.6f);
                var fc = _fadeVeil.color;
                fc.a = _fadeAlpha;
                _fadeVeil.color = fc;
            }

            var gm = GameManager.Instance;
            if (gm == null) return;

            // Title subtle alpha pulse
            if (gm.CurrentState == GameState.Title && _titlePromptText != null)
            {
                float a = Mathf.Lerp(0.45f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.8f));
                var c = _titlePromptText.color; c.a = a; _titlePromptText.color = c;
                return;
            }

            Vector3 playerPos = gm.GetActivePlayerPosition();

            // Update full map player marker if MapOpen
            if (gm.CurrentState == GameState.MapOpen && _fullMapPlayerDot != null)
            {
                _fullMapPlayerDot.anchoredPosition = WorldToMapPixel(playerPos, 560f);
                return;
            }

            if (gm.CurrentState != GameState.Playing) return;

            // 1. GTA V-Style Rotating Minimap + Orbiting North Compass + Stamina Bar
            if (_minimapImageRect != null)
            {
                Vector2 playerPx = WorldToMapPixel(playerPos, MinimapTexSize);
                _minimapImageRect.anchoredPosition = -playerPx;
            }
            float targetHeading = Camera.main != null ? Camera.main.transform.eulerAngles.y : gm.GetActivePlayerTransform().eulerAngles.y;
            _smoothMapYaw = Mathf.LerpAngle(_smoothMapYaw, targetHeading, Time.deltaTime * 10f);
            if (_minimapRotatePivot != null)
                _minimapRotatePivot.localRotation = Quaternion.Euler(0f, 0f, _smoothMapYaw);
            if (_minimapPlayerArrow != null)
            {
                float relHeading = Mathf.DeltaAngle(_smoothMapYaw, gm.GetActivePlayerTransform().eulerAngles.y);
                _minimapPlayerArrow.localRotation = Quaternion.Euler(0f, 0f, -relHeading);
            }
            if (_compassNorthRect != null)
            {
                float rad = _smoothMapYaw * Mathf.Deg2Rad;
                // Direction of world North (+Z) in rotated minimap screen space
                Vector2 dir = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
                float halfW = 124f;
                float halfH = 90f;
                float sx = Mathf.Abs(dir.x) > 0.001f ? halfW / Mathf.Abs(dir.x) : 999f;
                float sy = Mathf.Abs(dir.y) > 0.001f ? halfH / Mathf.Abs(dir.y) : 999f;
                _compassNorthRect.anchoredPosition = dir * Mathf.Min(sx, sy);
            }
            if (_staminaBarFill != null && gm.playerFoot != null)
                _staminaBarFill.fillAmount = gm.playerFoot.StaminaNormalized;
            if (_districtText != null)
                _districtText.text = gm.CurrentDistrict.ToUpperInvariant();

            // 2. Wanted Stars & Police radar blips
            var ws = gm.wantedSystem;
            int stars = ws != null ? ws.WantedStars : 0;
            bool evading = ws != null && ws.IsEvading;
            float flash = Mathf.Sin(Time.time * 10f);
            for (int i = 0; i < 5; i++)
            {
                if (_starIcons[i] == null) continue;
                if (i < stars)
                {
                    if (evading)
                        _starIcons[i].color = flash > 0f ? Palette.HudGoldAccent : new Color(0.35f, 0.42f, 0.52f, 0.35f);
                    else
                        _starIcons[i].color = flash > 0f ? Palette.HudWantedAlert : Palette.HudWantedPolice;
                }
                else
                {
                    _starIcons[i].color = new Color(0.25f, 0.32f, 0.42f, 0.38f);
                }
            }

            if (_wantedSubText != null)
            {
                _wantedSubText.text = stars == 0
                    ? "SIN BÚSQUEDA POLICIAL"
                    : (evading ? $"EVADIENDO POLICÍA ({Mathf.RoundToInt(ws.EvasionProgress * 100f)}%)" : "POLICÍA DE MALDONADO PERSIGUIENDO");
                _wantedSubText.color = stars == 0 ? Palette.HudTextMuted : (evading ? Palette.HudGoldAccent : Palette.HudWantedAlert);
            }

            if (_bustedBarRoot != null && ws != null)
            {
                bool showBust = ws.BustedProgress > 0.02f;
                _bustedBarRoot.SetActive(showBust);
                if (showBust && _bustedBarFill != null)
                    _bustedBarFill.fillAmount = ws.BustedProgress;
            }

            for (int i = 0; i < _policeMiniBlips.Count; i++)
            {
                bool active = ws != null && i < ws.ActivePolice.Count && ws.ActivePolice[i] != null;
                _policeMiniBlips[i].gameObject.SetActive(active);
                if (active)
                {
                    _policeMiniBlips[i].rectTransform.anchoredPosition = WorldToMapPixel(ws.ActivePolice[i].transform.position, MinimapTexSize);
                    _policeMiniBlips[i].color = ((i % 2 == 0) ? flash > 0f : flash <= 0f) ? Palette.HudWantedAlert : Palette.HudWantedPolice;
                }
            }

            // 3. Time of day, landmarks & Rambla Time Trial
            if (_timeText != null && gm.atmosphere != null)
                _timeText.text = gm.atmosphere.GetTimeLabel() + "  [T]";
            if (_discoveryText != null)
                _discoveryText.text = $"LUGARES ICÓNICOS: {gm.DiscoveredCount} / {gm.TotalLandmarks}";
            if (_trialText != null)
            {
                _trialText.text = gm.TimeTrialActive
                    ? $"CIRCUITO RAMBLA  ·  CP {gm.TimeTrialCheckpointIndex}/{gm.TimeTrialTotalCheckpoints}  ·  {gm.TimeTrialElapsed:0.0}s"
                    : "";
            }

            // 4. Speedometer & Contextual Interact Prompt
            var vi = gm.vehicleInteraction;
            bool inCar = vi != null && vi.IsInVehicle && vi.CurrentVehicle != null;
            if (_speedoRoot != null) _speedoRoot.SetActive(inCar);

            if (inCar)
            {
                var car = vi.CurrentVehicle;
                _speedValText.text = Mathf.RoundToInt(car.SpeedKmh).ToString();
                _gearText.text = car.CurrentGear < 0 ? "R" : (car.CurrentGear == 0 ? "N" : car.CurrentGear.ToString());
                _vehicleNameText.text = car.vehicleDisplayName.ToUpperInvariant();
                if (_rpmFill != null)
                {
                    _rpmFill.fillAmount = car.RpmNormalized;
                    _rpmFill.color = car.RpmNormalized > 0.85f ? Palette.HudWantedAlert : (car.RpmNormalized > 0.62f ? Palette.HudGoldAccent : Palette.HudCyanAccent);
                }
                string badge = car.IsDrifting ? "DRIFT" : (car.SirenActive ? "SIRENA" : (AudioManager.Instance != null ? AudioManager.Instance.RadioShortBadge : "RADIO OFF"));
                _vehicleBadgesText.text = badge;
            }

            if (_interactPromptRoot != null)
            {
                bool showPrompt = !inCar && vi != null && vi.NearbyVehicle != null;
                _interactPromptRoot.SetActive(showPrompt);
                if (showPrompt)
                    _interactPromptText.text = $"F — SUBIR A {vi.NearbyVehicle.vehicleDisplayName.ToUpperInvariant()}";
            }

            // 5. Toast fade timer
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.deltaTime;
                if (_toastTimer <= 0f && _toastRoot != null)
                    _toastRoot.SetActive(false);
            }
        }
    }
}
