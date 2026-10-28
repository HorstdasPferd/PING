using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PingMod.UI
{
    // Target selection window of a PING portal. Layout and idea follow the PlanetFinder mod (Unlicense),
    // but this is an independent implementation built from plain Unity UI parts, so PlanetFinder is not needed.
    //
    // Opens when the player clicks a PING portal. Lists planets by distance, filterable by
    // "has PING portal" (portal icon), "within range", "current star" and a search text.
    // Clicking a row starts the teleport. Targets that can never work (too far, no portal there) are greyed out.
    public class PingTargetWindow : MonoBehaviour
    {
        const float WindowWidth = 560f;
        const float WindowHeight = 504f;
        const float ListTop = 114f;
        const float ListWidth = 528f;
        const float RowHeight = 26f;
        const int VisibleRows = 12;
        const int ScrollStep = 3;

        // Alpha is replaced by the WindowOpacity config value.
        // Olive-khaki like the game's own windows (sampled from the in-game UI).
        // Colours taken from the game's own window data (panel-bg #002C37, border #A7E9FF, glass #61D8FF, storage #0E1A24, title #969696).
        // The game blurs what is behind its windows; we cannot, so the panel is simply translucent (WindowOpacity setting).
        static readonly Color PanelColor = new Color(0f, 0.035f, 0.045f, 0.80f); // much darker than #002C37 (alpha comes from WindowOpacity)
        static readonly Color GlassColor = new Color(0.38f, 0.847f, 1f, 0.05f);
        static readonly Color AccentLineColor = new Color(0.655f, 0.914f, 1f, 0.9f);
        const float FrameCut = 14f; // size of the cut corners (top left, bottom right)
        static readonly Color StorageColor = new Color(0.02f, 0.04f, 0.06f, 0.70f);
        static readonly Color TitleColor = new Color(0.588f, 0.588f, 0.588f, 1f);
        static readonly Color CountColor = new Color(0.38f, 0.847f, 1f, 0.78f);
        // Thin blue-teal frame like the game's own windows.
        static readonly Color FrameColor = new Color(0.655f, 0.914f, 1f, 0.55f); // game value is 29% (several layers); a 1 px line needs more
        static readonly Color AccentColor = new Color(0.99f, 0.59f, 0.37f, 1f);
        static readonly Color ButtonOff = new Color(0.38f, 0.847f, 1f, 0.13f);
        static readonly Color ButtonOn = new Color(0.99f, 0.59f, 0.37f, 0.85f);
        static readonly Color TextNormal = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color TextDim = new Color(1f, 1f, 1f, 0.3f);
        static readonly Color TextBad = new Color(1f, 0.35f, 0.3f, 0.95f);
        static readonly Color RowEven = new Color(0.655f, 0.914f, 1f, 0.07f);
        static readonly Color RowOdd = new Color(0.655f, 0.914f, 1f, 0.025f);

        static PingTargetWindow instance;

        // Window position chosen by dragging; kept while the game runs.
        static Vector2 savedPosition;
        static bool hasSavedPosition;

        class Entry
        {
            public PlanetData planet;
            public double distLy;
            public bool hasPortal;
            public bool sameStar;
            public long cost;
            public string label;
            public string searchKey;
        }

        class RowView
        {
            public GameObject root;
            public Image background;
            public PingHoverTint tint;
            public Text nameText;
            public Text distText;
            public Text costText;
            public Image portalIcon;
            public Entry entry;
        }

        Font font;
        RectTransform panelRect;
        Image panelImage;
        PingFrameGraphic panelFill;
        Text energyText;
        RectTransform energyFill;
        Text statusText;
        Text countText;
        Button teleportButton;
        PlanetData selected;
        InputField searchField;
        Image portalFilterImage;
        Image rangeFilterImage;
        Image starFilterImage;
        RectTransform scrollHandle;
        readonly List<RowView> rows = new List<RowView>();

        readonly List<Entry> entries = new List<Entry>();
        readonly List<Entry> view = new List<Entry>();
        PortalData origin;
        int originStarId;
        int scroll;
        bool filterPortal = true;
        bool filterRange = true;
        bool filterStar;
        bool pointerInside;
        Sprite portalSprite;

        public static bool IsOpen
        {
            get { return instance != null && instance.gameObject.activeSelf; }
        }

        // ------------------------------------------------------------------
        // Public entry points (called from the Harmony patches)
        // ------------------------------------------------------------------

        public static void Open(PortalData portal)
        {
            try
            {
                if (portal == null) return;
                if (instance == null) instance = Create();
                if (instance == null) return;

                instance.Show(portal);
            }
            catch (Exception ex) { PingPlugin.SafeError("PING target window: open failed", ex); }
        }

        // Closes the window. 'clearInspect' also deselects the portal, so clicking it again reopens the window.
        public static void Close(bool clearInspect = false)
        {
            try
            {
                if (instance == null || !instance.gameObject.activeSelf) return;
                instance.Hide(clearInspect);
            }
            catch (Exception ex) { PingPlugin.SafeError("PING target window: close failed", ex); }
        }

        // Called once per UI frame from UIGame._OnUpdate.
        public static void Tick()
        {
            try
            {
                if (instance == null || !instance.gameObject.activeSelf) return;
                if (GameMain.isPaused || !GameMain.isRunning) return;
                instance.DoTick();
            }
            catch (Exception ex) { PingPlugin.SafeError("PING target window: tick failed", ex); }
        }

        // ------------------------------------------------------------------
        // Creation
        // ------------------------------------------------------------------

        static PingTargetWindow Create()
        {
            UIGame uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            if (uiGame == null) return null;

            // Same parent as the vanilla function windows.
            Transform parent = uiGame.inserterWindow != null ? uiGame.inserterWindow.transform.parent : UIRoot.instance.overlayCanvas.transform;

            GameObject go = new GameObject("PingTargetWindow", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;

            PingTargetWindow window = go.AddComponent<PingTargetWindow>();
            window.panelRect = rect;
            window.Build(uiGame);
            go.SetActive(false);
            return window;
        }

        void Build(UIGame uiGame)
        {
            font = null;
            if (uiGame.assemblerWindow != null && uiGame.assemblerWindow.stateText != null)
                font = uiGame.assemblerWindow.stateText.font;
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            portalSprite = PingPlugin.PingItem != null ? PingPlugin.PingItem.iconSprite : null;

            // Background (also blocks clicks from reaching the world behind the window).
            // The rectangle itself is invisible and only blocks clicks; the visible shape has cut corners like the game's windows.
            panelImage = AddImage(panelRect, Color.clear, true);
            panelFill = AddFrame(NewRect("fill", panelRect, 0f, 0f, WindowWidth, WindowHeight), PingFrameGraphic.Kind.Fill, PanelColor, 0f);
            AddFrame(NewRect("glass", panelRect, 0f, 0f, WindowWidth, WindowHeight), PingFrameGraphic.Kind.Fill, GlassColor, 0f);
            AddFrame(NewRect("border", panelRect, 0f, 0f, WindowWidth, WindowHeight), PingFrameGraphic.Kind.Outline, FrameColor, 1.5f);
            AddFrame(NewRect("accents", panelRect, 0f, 0f, WindowWidth, WindowHeight), PingFrameGraphic.Kind.Accents, AccentLineColor, 3f);
            AddImage(NewRect("title-accent", panelRect, 16f, 40f, 56f, 2f), AccentColor, false);

            // The whole window can be dragged (like the game's windows). A drag that starts on the
            // background, the frame or the title moves it; clicks on buttons still work as clicks.
            PingDragHandle drag = panelRect.gameObject.AddComponent<PingDragHandle>();
            drag.target = panelRect;
            drag.onMoved = delegate
            {
                savedPosition = panelRect.anchoredPosition;
                hasSavedPosition = true;
            };

            Text title = AddText(NewRect("title", panelRect, 16f, 10f, 440f, 28f), "PING Portal - Select Destination", 18, TitleColor, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;

            Button closeButton = NewButton("close", panelRect, WindowWidth - 44f, 8f, 28f, 28f, "X", 16, ButtonOff);
            closeButton.onClick.AddListener(delegate { Hide(true); });

            // Energy bar
            RectTransform barBack = NewRect("energy-back", panelRect, 16f, 46f, ListWidth, 20f);
            AddImage(barBack, new Color(0f, 0f, 0f, 0.55f), false);
            energyFill = NewRect("energy-fill", barBack, 0f, 0f, 0f, 20f);
            AddImage(energyFill, new Color(0.99f, 0.59f, 0.37f, 0.65f), false);
            energyText = AddText(NewRect("energy-text", barBack, 0f, 0f, ListWidth, 20f), "", 14, TextNormal, TextAnchor.MiddleCenter);

            // Filters
            Button portalButton = NewButton("filter-portal", panelRect, 16f, 74f, 32f, 32f, portalSprite == null ? "P" : "", 16, ButtonOff);
            portalFilterImage = portalButton.GetComponent<Image>();
            if (portalSprite != null)
            {
                Image icon = AddImage(NewRect("icon", portalButton.transform, 2f, 2f, 28f, 28f), Color.white, false);
                icon.sprite = portalSprite;
            }
            portalButton.onClick.AddListener(delegate { filterPortal = !filterPortal; FiltersChanged(); });

            Button rangeButton = NewButton("filter-range", panelRect, 56f, 74f, 92f, 32f, "", 14, ButtonOff);
            rangeFilterImage = rangeButton.GetComponent<Image>();
            SetButtonLabel(rangeButton, "<= " + PingPlugin.MaxTravelDistanceLightYears.ToString("F0") + " ly", 14);
            rangeButton.onClick.AddListener(delegate { filterRange = !filterRange; FiltersChanged(); });

            Button starButton = NewButton("filter-star", panelRect, 156f, 74f, 100f, 32f, "This star", 14, ButtonOff);
            starFilterImage = starButton.GetComponent<Image>();
            starButton.onClick.AddListener(delegate { filterStar = !filterStar; FiltersChanged(); });

            BuildSearchField();

            // List
            RectTransform listRect = NewRect("list", panelRect, 16f, ListTop, ListWidth, RowHeight * VisibleRows);
            AddImage(listRect, StorageColor, true);
            for (int i = 0; i < VisibleRows; i++) rows.Add(BuildRow(listRect, i));

            scrollHandle = NewRect("scroll-handle", listRect, ListWidth - 5f, 0f, 4f, 40f);
            AddImage(scrollHandle, new Color(1f, 1f, 1f, 0.35f), false);

            statusText = AddText(NewRect("status", panelRect, 16f, ListTop + RowHeight * VisibleRows + 8f, 400f, 24f), "", 14, TextNormal, TextAnchor.MiddleLeft);
            countText = AddText(NewRect("count", panelRect, WindowWidth - 156f, ListTop + RowHeight * VisibleRows + 8f, 140f, 24f), "", 14, CountColor, TextAnchor.MiddleRight);

            teleportButton = NewButton("teleport", panelRect, WindowWidth - 156f, ListTop + RowHeight * VisibleRows + 40f, 140f, 34f, "Teleport", 16, ButtonOff);
            teleportButton.onClick.AddListener(delegate { OnTeleportClick(); });
        }

        void BuildSearchField()
        {
            RectTransform fieldRect = NewRect("search", panelRect, 264f, 74f, 280f, 32f);
            Image bg = AddImage(fieldRect, StorageColor, true);

            Text text = AddText(NewRect("text", fieldRect, 8f, 0f, 264f, 32f), "", 16, TextNormal, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.raycastTarget = false;
            Text placeholder = AddText(NewRect("placeholder", fieldRect, 8f, 0f, 264f, 32f), "Search planet or star...", 16, TextDim, TextAnchor.MiddleLeft);
            placeholder.fontStyle = FontStyle.Italic;

            searchField = fieldRect.gameObject.AddComponent<InputField>();
            searchField.targetGraphic = bg;
            searchField.textComponent = text;
            searchField.placeholder = placeholder;
            searchField.lineType = InputField.LineType.SingleLine;
            searchField.onValueChanged.AddListener(delegate (string value) { FiltersChanged(); });
        }

        RowView BuildRow(RectTransform parent, int index)
        {
            RowView row = new RowView();
            RectTransform rect = NewRect("row-" + index, parent, 0f, index * RowHeight, ListWidth - 8f, RowHeight - 2f);
            row.root = rect.gameObject;
            row.background = AddImage(rect, index % 2 == 0 ? RowEven : RowOdd, true);

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = row.background;
            button.transition = Selectable.Transition.None;
            PingHoverTint tint = rect.gameObject.AddComponent<PingHoverTint>();
            tint.image = row.background;
            tint.normal = index % 2 == 0 ? RowEven : RowOdd;
            row.tint = tint;
            int rowIndex = index;
            button.onClick.AddListener(delegate { OnRowClick(rowIndex); });

            PingRowHover hover = rect.gameObject.AddComponent<PingRowHover>();
            hover.onEnter = delegate { OnRowHover(rowIndex); };
            hover.onExit = delegate { ShowStatus("", false); };

            row.portalIcon = AddImage(NewRect("portal", rect, 6f, 3f, 20f, 20f), Color.white, false);
            row.portalIcon.sprite = portalSprite;
            row.nameText = AddText(NewRect("name", rect, 34f, 0f, 270f, RowHeight - 2f), "", 16, TextNormal, TextAnchor.MiddleLeft);
            row.distText = AddText(NewRect("dist", rect, 306f, 0f, 100f, RowHeight - 2f), "", 14, TextNormal, TextAnchor.MiddleRight);
            row.costText = AddText(NewRect("cost", rect, 412f, 0f, 104f, RowHeight - 2f), "", 14, TextNormal, TextAnchor.MiddleRight);
            return row;
        }

        // ------------------------------------------------------------------
        // Small UI helpers
        // ------------------------------------------------------------------

        static RectTransform NewRect(string name, Transform parent, float x, float y, float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            rect.localScale = Vector3.one;
            return rect;
        }

        static PingFrameGraphic AddFrame(RectTransform rect, PingFrameGraphic.Kind kind, Color color, float thickness)
        {
            PingFrameGraphic g = rect.gameObject.AddComponent<PingFrameGraphic>();
            g.kind = kind;
            g.thickness = thickness;
            g.cut = FrameCut;
            g.color = color;
            g.raycastTarget = false;
            return g;
        }

        static Image AddImage(Component target, Color color, bool raycast)
        {
            Image image = target.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        Text AddText(Component target, string content, int size, Color color, TextAnchor anchor)
        {
            Text text = target.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        Button NewButton(string name, Transform parent, float x, float y, float w, float h, string label, int size, Color color)
        {
            RectTransform rect = NewRect(name, parent, x, y, w, h);
            Image image = AddImage(rect, color, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            PingHoverTint tint = rect.gameObject.AddComponent<PingHoverTint>();
            tint.image = image;
            tint.normal = color;

            RectTransform labelRect = NewRect("label", rect, 0f, 0f, w, h);
            AddText(labelRect, label, size, TextNormal, TextAnchor.MiddleCenter);
            return button;
        }

        static void SetButtonLabel(Button button, string label, int size)
        {
            Text text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }

        // ------------------------------------------------------------------
        // Open / close
        // ------------------------------------------------------------------

        void Show(PortalData portal)
        {
            origin = portal;
            selected = null;

            PlanetData current = GameMain.galaxy != null ? GameMain.galaxy.PlanetById(portal.planetId) : null;
            originStarId = current != null && current.star != null ? current.star.id : 0;

            filterPortal = PingPlugin.RequirePaired;
            filterRange = true;
            filterStar = false;
            scroll = 0;
            searchField.text = "";

            BuildEntries();
            ApplyFilters();
            RefreshFilterButtons();
            RefreshHeader();
            ShowStatus("", false);

            Color panel = PanelColor;
            panel.a = PingPlugin.WindowAlpha;
            panelFill.color = panel;
            panelRect.anchoredPosition = hasSavedPosition ? savedPosition : Vector2.zero;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        void Hide(bool clearInspect)
        {
            gameObject.SetActive(false);
            pointerInside = false;

            if (EventSystem.current != null && searchField != null && searchField.isFocused)
                EventSystem.current.SetSelectedGameObject(null);

            if (clearInspect)
            {
                Player player = GameMain.mainPlayer;
                if (player != null && player.controller != null && player.controller.actionInspect != null)
                    player.controller.actionInspect.InspectNothing();
            }
        }

        // ------------------------------------------------------------------
        // Data
        // ------------------------------------------------------------------

        void BuildEntries()
        {
            entries.Clear();
            GalaxyData galaxy = GameMain.galaxy;
            if (galaxy == null || origin == null) return;

            PlanetData current = galaxy.PlanetById(origin.planetId);
            if (current == null) return;

            HashSet<int> withPortal = PingTicker.PlanetsWithPortals();
            for (int i = 0; i < galaxy.starCount; i++)
            {
                StarData star = galaxy.stars[i];
                if (star == null || star.planets == null) continue;

                for (int j = 0; j < star.planetCount; j++)
                {
                    PlanetData planet = star.planets[j];
                    if (planet == null || planet.id == origin.planetId) continue;

                    Entry entry = new Entry();
                    entry.planet = planet;
                    entry.distLy = PingTicker.DistanceLy(current, planet);
                    entry.hasPortal = withPortal.Contains(planet.id);
                    entry.sameStar = star.id == originStarId;
                    entry.cost = PingTicker.CostJoules(entry.distLy);
                    entry.label = (planet.type == EPlanetType.Gas ? "[GAS] " : "") + planet.displayName;
                    entry.searchKey = (planet.displayName + " " + star.displayName).ToLowerInvariant();
                    entries.Add(entry);
                }
            }
            entries.Sort(delegate (Entry a, Entry b)
            {
                int c = a.distLy.CompareTo(b.distLy);
                return c != 0 ? c : a.planet.id.CompareTo(b.planet.id);
            });
        }

        void ApplyFilters()
        {
            string query = searchField != null ? searchField.text.Trim().ToLowerInvariant() : "";
            double maxLy = PingPlugin.MaxTravelDistanceLightYears;

            view.Clear();
            foreach (Entry entry in entries)
            {
                if (filterPortal && !entry.hasPortal) continue;
                if (filterRange && entry.distLy > maxLy) continue;
                if (filterStar && !entry.sameStar) continue;
                if (query.Length > 0 && entry.searchKey.IndexOf(query, StringComparison.Ordinal) < 0) continue;
                view.Add(entry);
            }
            ClampScroll();
            RefreshRows();
        }

        // Null when the target can be used, otherwise the reason it can not.
        static string Reason(Entry entry)
        {
            double maxLy = PingPlugin.MaxTravelDistanceLightYears;
            if (entry.distLy > maxLy) return "Too far: " + entry.distLy.ToString("F1") + " ly (maximum " + maxLy.ToString("F0") + " ly).";
            if (PingPlugin.RequirePaired && !entry.hasPortal) return "There is no PING portal on this planet.";
            return null;
        }

        // ------------------------------------------------------------------
        // Display
        // ------------------------------------------------------------------

        void FiltersChanged()
        {
            scroll = 0;
            ShowStatus("", false);
            RefreshFilterButtons();
            ApplyFilters();
        }

        void RefreshFilterButtons()
        {
            SetToggleColor(portalFilterImage, filterPortal);
            SetToggleColor(rangeFilterImage, filterRange);
            SetToggleColor(starFilterImage, filterStar);
        }

        static void SetToggleColor(Image image, bool on)
        {
            Color color = on ? ButtonOn : ButtonOff;
            PingHoverTint tint = image.GetComponent<PingHoverTint>();
            if (tint != null) tint.normal = color;
            image.color = color;
        }

        void ClampScroll()
        {
            int max = Mathf.Max(0, view.Count - VisibleRows);
            scroll = Mathf.Clamp(scroll, 0, max);
        }

        void RefreshRows()
        {
            long stored = 0;
            if (origin != null) PingTicker.TryGetStored(origin, out stored);

            for (int i = 0; i < rows.Count; i++)
            {
                RowView row = rows[i];
                int index = scroll + i;
                if (index >= view.Count)
                {
                    row.entry = null;
                    row.background.color = i % 2 == 0 ? RowEven : RowOdd;
                    row.root.SetActive(false);
                    continue;
                }

                Entry entry = view[index];
                row.entry = entry;
                row.root.SetActive(true);

                bool usable = Reason(entry) == null;
                Color normal = usable ? TextNormal : TextDim;

                Color baseColor = i % 2 == 0 ? RowEven : RowOdd;
                if (entry.planet == selected) baseColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.30f);
                row.tint.normal = baseColor;
                row.background.color = baseColor;

                row.nameText.text = entry.label;
                row.nameText.color = normal;
                row.distText.text = entry.sameStar ? "same star" : entry.distLy.ToString("F1") + " ly";
                row.distText.color = normal;
                row.costText.text = (entry.cost / 1000000L) + " MJ";
                row.costText.color = !usable ? TextDim : (stored < entry.cost ? TextBad : TextNormal);
                row.portalIcon.enabled = entry.hasPortal;
                row.portalIcon.color = usable ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            }

            RefreshScrollBar();
            RefreshTeleportButton();
            countText.text = view.Count == 0 ? "" : (scroll + 1) + "-" + Mathf.Min(view.Count, scroll + VisibleRows) + " of " + view.Count;

            if (view.Count == 0)
            {
                ShowStatus(filterPortal
                    ? "No matching planet with a PING portal. Build a portal on the destination planet first."
                    : "No matching planet.", true);
            }
        }

        void RefreshScrollBar()
        {
            float trackHeight = RowHeight * VisibleRows;
            bool needed = view.Count > VisibleRows;
            scrollHandle.gameObject.SetActive(needed);
            if (!needed) return;

            float handleHeight = Mathf.Max(24f, trackHeight * VisibleRows / view.Count);
            float range = trackHeight - handleHeight;
            float fraction = (float)scroll / (view.Count - VisibleRows);
            scrollHandle.sizeDelta = new Vector2(4f, handleHeight);
            scrollHandle.anchoredPosition = new Vector2(ListWidth - 5f, -range * fraction);
        }

        void RefreshHeader()
        {
            if (origin == null) return;

            long stored;
            long max = PingPlugin.MaxEnergyJoules;
            if (!PingTicker.TryGetStored(origin, out stored))
            {
                energyText.text = "Portal not ready";
                energyFill.sizeDelta = new Vector2(0f, 20f);
                return;
            }

            float fraction = max > 0 ? Mathf.Clamp01((float)((double)stored / max)) : 0f;
            energyFill.sizeDelta = new Vector2(ListWidth * fraction, 20f);

            string text = (stored / 1e9).ToString("F2") + " / " + (max / 1e9).ToString("F1") + " GJ  (" + (fraction * 100f).ToString("F0") + "%)";
            double cooldown = PingTicker.CooldownSecondsLeft(origin);
            if (cooldown > 0) text += "   cooldown " + cooldown.ToString("F0") + " s";
            energyText.text = text;
        }

        void RefreshTeleportButton()
        {
            if (teleportButton == null) return;
            Entry entry = FindSelectedEntry();
            bool ready = entry != null && Reason(entry) == null;
            Text label = teleportButton.GetComponentInChildren<Text>();
            if (label != null) label.color = ready ? TextNormal : TextDim;
        }

        Entry FindSelectedEntry()
        {
            if (selected == null) return null;
            for (int i = 0; i < view.Count; i++)
                if (view[i].planet == selected) return view[i];
            return null;
        }

        void ShowStatus(string message, bool warning)
        {
            if (statusText == null) return;
            statusText.text = message;
            statusText.color = warning ? AccentColor : TextNormal;
        }

        // ------------------------------------------------------------------
        // Interaction
        // ------------------------------------------------------------------

        void OnRowHover(int rowIndex)
        {
            RowView row = rows[rowIndex];
            if (row.entry == null) return;

            string reason = Reason(row.entry);
            if (reason != null) ShowStatus(reason, true);
            else ShowStatus("Click to select (" + (row.entry.cost / 1000000L) + " MJ, " + row.entry.distLy.ToString("F1") + " ly).", false);
        }

        void OnRowClick(int rowIndex)
        {
            RowView row = rows[rowIndex];
            if (row.entry == null || origin == null) return;

            selected = row.entry.planet;
            string reason = Reason(row.entry);
            if (reason != null) ShowStatus(reason, true);
            RefreshRows();
        }

        void OnTeleportClick()
        {
            if (origin == null) return;
            Entry entry = FindSelectedEntry();
            if (entry == null)
            {
                ShowStatus("Select a destination first.", true);
                return;
            }

            string reason = Reason(entry);
            if (reason != null)
            {
                ShowStatus(reason, true);
                return;
            }

            string error = PingTicker.TryStartTeleport(origin, entry.planet);
            if (error != null)
            {
                ShowStatus(error, true);
                return;
            }
            Hide(true);
        }

        void DoTick()
        {
            if (origin == null || !PortalStillExists())
            {
                Hide(false);
                return;
            }

            // Escape: leave the search field first, then close the window.
            if (VFInput.escape)
            {
                if (searchField.isFocused)
                {
                    VFInput.UseEscape();
                    EventSystem.current.SetSelectedGameObject(null);
                }
                else if (!VFInput.inputing)
                {
                    VFInput.UseEscape();
                    Hide(true);
                    return;
                }
            }

            Camera cam = UIRoot.instance.overlayCanvas != null ? UIRoot.instance.overlayCanvas.worldCamera : null;
            pointerInside = RectTransformUtility.RectangleContainsScreenPoint(panelRect, Input.mousePosition, cam);
            if (pointerInside)
            {
                // Keeps the mouse wheel from zooming the camera while it scrolls the list.
                VFInput.inScrollView = true;
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    scroll -= (wheel > 0f ? 1 : -1) * ScrollStep;
                    ClampScroll();
                    RefreshRows();
                }
            }

            int frame = Time.frameCount;
            if (frame % 15 == 0) RefreshHeader();
            if (frame % 30 == 0) RefreshRows();
        }

        bool PortalStillExists()
        {
            PortalData still = PingTicker.GetPortal(origin.planetId, origin.entityId);
            return still != null && still == origin;
        }

        void FixedUpdate()
        {
            if (pointerInside) VFInput.inScrollView = true;
        }
    }

    // Lets the window be moved by dragging its title bar. Keeps the title bar on screen.
    public class PingDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform target;
        public Action onMoved;

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null) return;

            Canvas canvas = target.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            Vector2 position = target.anchoredPosition + eventData.delta / scale;

            RectTransform parent = target.parent as RectTransform;
            if (parent != null)
            {
                float halfW = target.sizeDelta.x * 0.5f;
                float halfH = target.sizeDelta.y * 0.5f;
                float parentW = parent.rect.width * 0.5f;
                float parentH = parent.rect.height * 0.5f;
                position.x = Mathf.Clamp(position.x, -parentW - halfW + 120f, parentW + halfW - 120f);
                position.y = Mathf.Clamp(position.y, -parentH - halfH + 60f, parentH - halfH);
            }
            target.anchoredPosition = position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (onMoved != null) onMoved();
        }
    }

    // Lightens a button or row while the mouse is over it (UI tint can only darken, so this is done by hand).
    public class PingHoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image image;
        public Color normal;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (image == null) return;
            image.color = new Color(
                Mathf.Min(1f, normal.r + 0.18f),
                Mathf.Min(1f, normal.g + 0.18f),
                Mathf.Min(1f, normal.b + 0.18f),
                Mathf.Min(1f, normal.a + 0.18f));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (image != null) image.color = normal;
        }
    }

    // Reports mouse enter / exit of a list row (used for the status line).
    public class PingRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action onEnter;
        public Action onExit;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (onEnter != null) onEnter();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (onExit != null) onExit();
        }
    }
    // A rectangle with two cut corners (top left, bottom right) as fill, outline or corner accents.
    public class PingFrameGraphic : MaskableGraphic
    {
        public enum Kind { Fill, Outline, Accents }

        public Kind kind;
        public float thickness = 1f;
        public float cut = 14f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float c = Mathf.Min(cut, Mathf.Min(r.width, r.height) * 0.4f);

            // clockwise from the top edge; the cuts are the edges 5-0 and 2-3
            Vector2[] p =
            {
                new Vector2(r.xMin + c, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin + c),
                new Vector2(r.xMax - c, r.yMin), new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax - c)
            };
            Vector2 centre = r.center;

            if (kind == Kind.Fill)
            {
                vh.AddVert(centre, color, Vector2.zero);
                for (int i = 0; i < p.Length; i++) vh.AddVert(p[i], color, Vector2.zero);
                for (int i = 0; i < p.Length; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % p.Length);
            }
            else if (kind == Kind.Outline)
            {
                for (int i = 0; i < p.Length; i++) Edge(vh, p[i], p[(i + 1) % p.Length], thickness, centre);
            }
            else
            {
                Edge(vh, p[5], p[0], thickness, centre);
                Edge(vh, p[2], p[3], thickness, centre);
                const float len = 22f;
                Edge(vh, p[1] + new Vector2(-len, 0f), p[1], thickness, centre);
                Edge(vh, p[1], p[1] + new Vector2(0f, -len), thickness, centre);
                Edge(vh, p[4], p[4] + new Vector2(len, 0f), thickness, centre);
                Edge(vh, p[4], p[4] + new Vector2(0f, len), thickness, centre);
            }
        }

        // A line from a to b, thickness t, drawn on the inner side (towards the centre).
        void Edge(VertexHelper vh, Vector2 a, Vector2 b, float t, Vector2 centre)
        {
            Vector2 dir = (b - a).normalized;
            Vector2 n = new Vector2(-dir.y, dir.x);
            if (Vector2.Dot(n, centre - (a + b) * 0.5f) < 0f) n = -n;

            int i = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(b + n * t, color, Vector2.zero);
            vh.AddVert(a + n * t, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}