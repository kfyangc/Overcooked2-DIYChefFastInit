using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Team17.Online;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    public sealed class ChefGridMenu : MonoBehaviour
    {
        static ConfigEntry<bool> configured;
        static ConfigEntry<int> configuredColumns, configuredRows;
        static ConfigEntry<float> portraitYaw;
        static readonly MethodInfo fixedValue = AccessTools.Method(typeof(PlayerInputLookup), "GetFixedValue",
            new[] { typeof(PlayerInputLookup.LogicalValueID), typeof(PlayerGameInput) });
        internal static ChefGridMenu Active;
        static readonly Color Ink = new Color(0.16f, 0.28f, 0.36f);
        static readonly Color Paper = new Color(0.96f, 0.9f, 0.91f);
        static readonly Color[] PlayerColours = { new Color(0.15f, 0.72f, 1), new Color(1, 0.36f, 0.3f),
            new Color(0.3f, 0.85f, 0.4f), new Color(1, 0.78f, 0.15f) };

        sealed class Cursor
        {
            internal long Key;
            internal int Player, Index;
            internal bool Chosen;
            internal FrontendChefCustomisation View;
            internal PlayerGameInput Input;
            internal readonly ILogicalButton[] Buttons = new ILogicalButton[6];
            internal float RepeatAt;
            internal int Direction = -1;
        }

        sealed class Cell
        {
            internal GameObject Root;
            internal Image[] Highlight;
            internal Image Shade;
            internal RawImage Portrait;
            internal int PlayerMask;
        }

        FrontendChefMenu owner;
        List<ChefGridEntry> entries;
        readonly List<Cursor> cursors = new List<Cursor>();
        readonly HashSet<long> alive = new HashSet<long>();
        readonly List<ChefGridEntry> wanted = new List<ChefGridEntry>();
        ChefGridLayout layout;
        Cell[] cells;
        int Columns { get { return layout.Columns; } }
        int PageSize { get { return layout.PageSize; } }
        readonly Text[] playerLabels = new Text[4];
        readonly Image[] playerTabs = new Image[4];
        GameObject canvasObject;
        RectTransform frame;
        RectTransform gridLayer;
        Font font;
        Text pageLabel, detailName, detailKind, detailPosition, detailStatus;
        RawImage detailPortrait;
        Image previousPage, nextPage;
        float detailReadyAt;
        ChefGridPortraits portraits;
        Cursor focused;
        int page, openedFrame;
        bool open, failed, ownsFont;
        bool Chinese { get { return Localization.GetLanguage() == SupportedLanguages.Chinese; } }
        string L(string zh, string en) { return Chinese ? zh : en; }
        internal bool IsOpen { get { return open; } }

        internal static void Configure(ConfigFile config)
        {
            configured = config.Bind("ChefSelection", "GridEnabled", true,
                "Show a paged portrait grid in the frontend chef menu. Disable to use the original carousel.");
            configuredColumns = config.Bind("ChefSelection", "GridColumns", 10,
                new ConfigDescription("Chef grid columns. Applied next time the chef menu opens.",
                    new AcceptableValueRange<int>(ChefGridLayout.MinColumns, ChefGridLayout.MaxColumns)));
            configuredRows = config.Bind("ChefSelection", "GridRows", 5,
                new ConfigDescription("Chef grid rows. Applied next time the chef menu opens.",
                    new AcceptableValueRange<int>(ChefGridLayout.MinRows, ChefGridLayout.MaxRows)));
            portraitYaw = config.Bind("ChefSelection", "PortraitYaw", 0f,
                "Rotation in degrees of generated portraits; changing this rebuilds their visual cache.");
        }

        internal static void Show(FrontendChefMenu menu)
        {
            if (configured == null || !configured.Value) return;
            var grid = menu.GetComponent<ChefGridMenu>() ?? menu.gameObject.AddComponent<ChefGridMenu>();
            if (grid.failed) return;
            try { grid.Open(menu); }
            catch (Exception ex)
            {
                grid.failed = true;
                grid.CloseGrid();
                DIYChefPlugin.Warn("Chef grid unavailable; original menu retained: " + ex);
            }
        }

        void Open(FrontendChefMenu menu)
        {
            CloseGrid();
            owner = menu;
            layout = new ChefGridLayout(configuredColumns.Value, configuredRows.Value);
            cells = new Cell[PageSize];
            var progress = GameUtils.GetMetaGameProgress();
            if (progress == null) return;
            entries = ChefGridRoster.Build(GameUtils.GetAvatarDirectoryData().Avatars, progress.GetUnlockedAvatars());
            if (entries.Count == 0) return;
            SyncPlayers();
            if (focused == null) return;
            BuildUI();
            try { portraits = new ChefGridPortraits(focused.View, portraitYaw.Value); }
            catch (Exception ex) { DIYChefPlugin.Warn("Portrait studio unavailable; chef names remain selectable: " + ex); }
            page = focused.Index / PageSize;
            openedFrame = Time.frameCount;
            open = true;
            Active = this;
            PreparePortraits();
            Refresh();
            DIYChefPlugin.Log("Chef grid opened entries=" + entries.Count + " layout=" + layout.Columns + "x" + layout.Rows +
                " pages=" + ChefGridNavigation.PageCount(entries.Count, PageSize));
        }

        void SyncPlayers()
        {
            var users = ClientUserSystem.m_Users;
            alive.Clear();
            for (int i = 0; i < users.Count && i < owner.m_PlayerChefs.Length && i < 4; i++)
            {
                var user = users._items[i];
                var view = owner.m_PlayerChefs[i];
                if (!user.IsLocal || view == null || !view.IsActive()) continue;
                long key = ChefGridNavigation.PlayerKey((int)user.Engagement, (int)user.Split);
                alive.Add(key);
                var cursor = cursors.Find(x => x.Key == key);
                if (cursor == null)
                {
                    int index = ChefGridRoster.Find(entries, i);
                    cursor = new Cursor { Key = key, Index = Math.Max(0, index), Chosen = index >= 0 };
                    cursors.Add(cursor);
                }
                cursor.Player = i;
                cursor.View = view;
                var input = view.GetAssignedInput();
                if (cursor.Input != input)
                {
                    cursor.Input = input;
                    var ids = new[] { PlayerInputLookup.LogicalButtonID.UILeftPlayerSpecific,
                        PlayerInputLookup.LogicalButtonID.UIRightPlayerSpecific, PlayerInputLookup.LogicalButtonID.UIUp,
                        PlayerInputLookup.LogicalButtonID.UIDown, PlayerInputLookup.LogicalButtonID.UISelect,
                        PlayerInputLookup.LogicalButtonID.UICancel };
                    for (int n = 0; n < ids.Length; n++)
                        cursor.Buttons[n] = PlayerInputLookup.GetFixedButton(ids[n], input);
                    // Unlike UIUp/UIDown, MovementY respects the two sides of a split keyboard.
                    var vertical = (ILogicalValue)fixedValue.Invoke(null,
                        new object[] { PlayerInputLookup.LogicalValueID.MovementY, input });
                    cursor.Buttons[2] = new ValueThresholdButton(vertical, ValueThresholdButton.ThresholdType.LessThan, -0.75f);
                    cursor.Buttons[3] = new ValueThresholdButton(vertical, ValueThresholdButton.ThresholdType.Greater, 0.75f);
                }
            }
            cursors.RemoveAll(x => !alive.Contains(x.Key));
            if (focused == null || !cursors.Contains(focused)) focused = cursors.Count == 0 ? null : cursors[0];
        }

        internal void Tick()
        {
            if (!open) return;
            try { TickCore(); }
            catch (Exception ex)
            {
                failed = true;
                CloseGrid();
                DIYChefPlugin.Warn("Chef grid closed after an error; original menu retained: " + ex);
            }
        }

        void TickCore()
        {
            SyncPlayers();
            if (focused == null) { CloseGrid(); return; }
            if (Time.frameCount <= openedFrame + 1 || T17DialogBoxManager.HasAnyOpenDialogs() ||
                (T17FrontendFlow.Instance != null && T17FrontendFlow.Instance.IsCameraTransitioning()))
            {
                foreach (var cursor in cursors) foreach (var button in cursor.Buttons) Claim(button);
                PreparePortraits();
                Refresh();
                return;
            }
            // Read all press events before claiming them: shared keyboard/split-pad bindings can overlap.
            bool cancel = false, accept = false;
            foreach (var cursor in cursors)
            {
                cancel |= cursor.Buttons[5].HasUnclaimedPressEvent();
                accept |= cursor.Buttons[4].HasUnclaimedPressEvent();
            }
            foreach (var cursor in cursors)
            {
                int direction = -1;
                for (int n = 0; n < 4; n++) if (cursor.Buttons[n].IsDown()) { direction = n; break; }
                if (cursor == focused && KeyboardUtils.IsKeyboard(cursor.Input))
                {
                    if (Input.GetKey(KeyCode.LeftArrow)) direction = 0;
                    else if (Input.GetKey(KeyCode.RightArrow)) direction = 1;
                    else if (Input.GetKey(KeyCode.UpArrow)) direction = 2;
                    else if (Input.GetKey(KeyCode.DownArrow)) direction = 3;
                }
                if (direction >= 0 && (cursor.Direction != direction || Time.unscaledTime >= cursor.RepeatAt))
                {
                    cursor.RepeatAt = Time.unscaledTime + (cursor.Direction != direction ? 0.35f : 0.1f);
                    int dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
                    int dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;
                    Select(cursor, ChefGridNavigation.Move(cursor.Index, entries.Count, Columns, dx, dy));
                }
                cursor.Direction = direction;
            }
            foreach (var cursor in cursors) foreach (var button in cursor.Buttons) Claim(button);
            if (cancel) { owner.Close(); return; }
            if (accept) { Accept(); return; }
            if (KeyboardUtils.IsKeyboard(focused.Input))
            {
                if (Input.GetKeyDown(KeyCode.PageUp)) page = ChefGridNavigation.TurnPage(page, -1, entries.Count, PageSize);
                if (Input.GetKeyDown(KeyCode.PageDown)) page = ChefGridNavigation.TurnPage(page, 1, entries.Count, PageSize);
                if (Input.GetKeyDown(KeyCode.Return)) { Accept(); return; }
            }
            Mouse();
            if (!open) return;
            PreparePortraits();
            Refresh();
        }

        void PreparePortraits()
        {
            if (portraits != null && focused != null)
            {
                wanted.Clear();
                wanted.Add(entries[focused.Index]);
                int end = Math.Min(entries.Count, (page + 1) * PageSize);
                for (int i = page * PageSize; i < end; i++)
                    if (i != focused.Index) wanted.Add(entries[i]);
                portraits.Tick(wanted);
            }
        }

        static void Claim(ILogicalButton button)
        {
            if (button == null) return;
            button.ClaimPressEvent();
            button.ClaimReleaseEvent();
        }

        void Select(Cursor cursor, int index)
        {
            if (index < 0 || index >= entries.Count) return;
            focused = cursor;
            page = index / PageSize;
            if (cursor.Index == index && cursor.Chosen) return;
            if (ChefGridRoster.Select(entries[index], cursor.Player, cursor.View))
            {
                cursor.Index = index;
                cursor.Chosen = true;
                GameUtils.TriggerAudio(GameOneShotAudioTag.UIButtonDown, LayerMask.NameToLayer("Administration"));
            }
        }

        void Accept()
        {
            Select(focused, focused.Index);
            owner.Hide();
        }

        void Mouse()
        {
            Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, Input.mousePosition, null, out point)) return;
            float x = point.x + 640, y = 360 - point.y;
            if (x < 0 || x > 1280 || y < 0 || y > 720) return;
            if (Input.mouseScrollDelta.y != 0)
                page = ChefGridNavigation.TurnPage(page, Input.mouseScrollDelta.y > 0 ? -1 : 1, entries.Count, PageSize);
            if (!Input.GetMouseButtonDown(0)) return;
            if (y >= 114 && y < 148)
            {
                foreach (var cursor in cursors)
                    if (x >= 48 + cursor.Player * 164 && x < 204 + cursor.Player * 164)
                    { focused = cursor; page = cursor.Index / PageSize; }
            }
            int hit = layout.HitTest(x, y, page, entries.Count);
            if (hit >= 0) Select(focused, hit);
            if (y >= 608 && y < 640)
            {
                if (x >= 48 && x < 184) page = ChefGridNavigation.TurnPage(page, -1, entries.Count, PageSize);
                if (x >= 800 && x < 936) page = ChefGridNavigation.TurnPage(page, 1, entries.Count, PageSize);
            }
            if (y >= 650 && y < 686)
            {
                if (x >= 904 && x < 1056) owner.Close();
                if (x >= 1072 && x < 1232) Accept();
            }
        }

        void BuildUI()
        {
            // Use a readable body font: the game's decorative font loses detail in small names.
            font = Font.CreateDynamicFontFromOSFont(Chinese ?
                new[] { "Microsoft YaHei", "SimHei", "Arial" } : new[] { "Segoe UI", "Arial" }, 24);
            ownsFont = true;
            canvasObject = new GameObject("DIYChef grid", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            // Keep this a root canvas. A canvas below the original menu inherits its transform scale
            // and becomes a nested canvas, so CanvasScaler cannot size it to the actual screen.
            if (owner.CachedEventSystem != null) owner.CachedEventSystem.SetSelectedGameObject(null);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(canvasObject.transform, false);
            var backRect = (RectTransform)backdrop.transform;
            backRect.anchorMin = Vector2.zero; backRect.anchorMax = Vector2.one;
            backRect.offsetMin = backRect.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = new Color(0.04f, 0.08f, 0.12f, 0.78f);
            frame = Rect(canvasObject.transform, "Frame", 0, 0, 1280, 720);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            Box(frame, "Panel border", 24, 24, 1232, 672, Ink);
            Box(frame, "Panel", 28, 28, 1224, 664, Paper);
            Box(frame, "Header", 40, 40, 1200, 58, Ink);
            Label(frame, "Title", L("选择厨师", "CHEFS"), 56, 42, 650, 54, 34, Color.white);
            Label(frame, "Total", entries.Count + L(" 位厨师", " CHEFS"), 1016, 49, 208, 40, 22, Color.white);
            for (int i = 0; i < 4; i++)
            {
                playerTabs[i] = Box(frame, "Player " + i, 48 + i * 164, 114, 156, 34, Ink);
                playerLabels[i] = Label(playerTabs[i].transform, "Label", "P" + (i + 1), 6, 0, 144, 34, 19, Color.white);
            }
            gridLayer = Rect(frame, "Chef grid", 0, 0, 1280, 720);
            for (int i = 0; i < PageSize; i++)
            {
                var rect = layout.Cell(i);
                float side = rect.Size;
                var cell = cells[i] = new Cell();
                cell.Root = Box(gridLayer, "Chef " + i, rect.X, rect.Y, side, side, Ink).gameObject;
                Box(cell.Root.transform, "Card", 1, 1, side - 2, side - 2, Paper);
                cell.Portrait = Rect(cell.Root.transform, "Portrait", 1, 1, side - 2, side - 2).gameObject.AddComponent<RawImage>();
                cell.Portrait.raycastTarget = false;
                cell.Shade = Box(cell.Root.transform, "Selection shade", 1, 1, side - 2, side - 2, Color.clear);
                // Extend outward so the selected thumbnail remains completely inside the frame.
                float stroke = Math.Min(6, side * 0.1f);
                cell.Highlight = new[] {
                    Box(cell.Root.transform, "Selected top", -stroke, -stroke, side + stroke * 2, stroke, Ink),
                    Box(cell.Root.transform, "Selected right", side, 0, stroke, side, Ink),
                    Box(cell.Root.transform, "Selected bottom", -stroke, side, side + stroke * 2, stroke, Ink),
                    Box(cell.Root.transform, "Selected left", -stroke, 0, stroke, side, Ink) };
            }
            Box(frame, "Preview panel", 952, 114, 280, 486, Paper);
            detailPortrait = Rect(frame, "Selected portrait", 964, 126, 256, 256).gameObject.AddComponent<RawImage>();
            detailPortrait.raycastTarget = false;
            detailStatus = Label(frame, "Preview status", "", 964, 230, 256, 48, 20, Ink);
            detailName = Label(frame, "Selected name", "", 964, 398, 256, 84, 26, Ink);
            detailName.resizeTextForBestFit = true;
            detailName.resizeTextMinSize = 20;
            detailName.resizeTextMaxSize = 26;
            detailKind = Label(frame, "Selected kind", "", 964, 500, 256, 36, 20, Ink);
            detailPosition = Label(frame, "Selected index", "", 964, 548, 256, 36, 22, Ink);
            previousPage = ButtonVisual(frame, "Previous page", L("< 上一页", "< PREV"), 48, 608, 136, 32);
            nextPage = ButtonVisual(frame, "Next page", L("下一页 >", "NEXT >"), 800, 608, 136, 32);
            pageLabel = Label(frame, "Page", "", 200, 608, 584, 32, 19, Ink);
            Label(frame, "Help", L("方向键 / 摇杆移动　滚轮 / PgUp PgDn 翻页　点击头像选择", "D-pad / stick: move   Wheel / PgUp PgDn: page   Click: choose"), 48, 650, 840, 36, 18, Ink);
            ButtonVisual(frame, "Back", L("返回 / B", "BACK / B"), 904, 650, 152, 36);
            ButtonVisual(frame, "Accept", L("确认 / A", "ACCEPT / A"), 1072, 650, 160, 36);
        }

        void Refresh()
        {
            for (int i = 0; i < 4; i++)
            {
                var cursor = cursors.Find(x => x.Player == i);
                playerTabs[i].gameObject.SetActive(cursor != null);
                if (cursor == null) continue;
                playerTabs[i].color = cursor == focused ? PlayerColours[i] : Ink;
                playerLabels[i].text = "P" + (i + 1) + (cursor == focused ? L("  正在选择", "  SELECTING") : "");
            }
            bool reorder = false;
            for (int slot = 0; slot < PageSize; slot++)
            {
                int index = page * PageSize + slot;
                var cell = cells[slot];
                cell.Root.SetActive(index < entries.Count);
                if (index >= entries.Count)
                {
                    reorder |= cell.PlayerMask != 0;
                    cell.PlayerMask = 0;
                    continue;
                }
                var entry = entries[index];
                var texture = portraits == null ? null : portraits.Get(entry);
                cell.Portrait.texture = texture;
                cell.Portrait.enabled = texture != null;
                int players = 0;
                foreach (var cursor in cursors)
                    if (cursor.Index == index) players |= 1 << cursor.Player;
                reorder |= cell.PlayerMask != players;
                cell.PlayerMask = players;
                int count = 0;
                for (int player = 0; player < 4; player++) if ((players & (1 << player)) != 0) count++;
                cell.Shade.color = count == 0 ? new Color(0, 0, 0, 0.22f) : new Color(1, 1, 1, 0.06f);
                for (int edge = 0; edge < cell.Highlight.Length; edge++)
                {
                    cell.Highlight[edge].enabled = count != 0;
                    if (count == 0) continue;
                    // Shared selections retain each player's color without text over faces.
                    int ordinal = edge % count;
                    for (int player = 0; player < 4; player++)
                        if ((players & (1 << player)) != 0 && ordinal-- == 0)
                        {
                            cell.Highlight[edge].color = PlayerColours[player];
                            break;
                        }
                }
            }
            if (reorder)
                foreach (var cell in cells)
                    if (cell.PlayerMask != 0) cell.Root.transform.SetAsLastSibling();
            // Lift the focused thumbnail with its exterior frame above neighboring cells.
            if (focused.Index / PageSize == page)
            {
                var selectedRoot = cells[focused.Index % PageSize].Root.transform;
                if (selectedRoot.GetSiblingIndex() != gridLayer.childCount - 1) selectedRoot.SetAsLastSibling();
            }
            var selected = entries[focused.Index];
            var detail = portraits == null ? null : portraits.GetDetail(selected);
            if (detail != detailPortrait.texture) detailReadyAt = Time.unscaledTime;
            detailPortrait.texture = detail;
            detailPortrait.enabled = detail != null;
            if (detail != null) detailPortrait.color = new Color(1, 1, 1, Mathf.Clamp01((Time.unscaledTime - detailReadyAt) / 0.15f));
            detailStatus.enabled = detail == null;
            detailStatus.text = portraits == null ? L("预览暂不可用", "PREVIEW UNAVAILABLE") : portraits.DetailFailed(selected) ?
                L("预览加载失败", "PREVIEW FAILED") : L("正在加载预览…", "LOADING PREVIEW…");
            detailName.text = selected.Label;
            detailKind.text = selected.CustomID == 255 ? L("官方厨师", "ORIGINAL CHEF") : L("额外厨师", "CUSTOM CHEF");
            detailPosition.text = (focused.Index + 1) + " / " + entries.Count;
            pageLabel.text = L("第 ", "PAGE ") + (page + 1) + " / " + ChefGridNavigation.PageCount(entries.Count, PageSize) +
                "     ·     " + L("光标移动时自动翻页", "AUTO PAGE ON MOVE");
            previousPage.color = page > 0 ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.35f);
            nextPage.color = page + 1 < ChefGridNavigation.PageCount(entries.Count, PageSize) ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.35f);
        }

        static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x + w / 2, -y - h / 2);
            return rect;
        }

        static Image Box(Transform parent, string name, float x, float y, float w, float h, Color colour)
        {
            var box = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            box.color = colour;
            box.raycastTarget = false;
            return box;
        }

        Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size, Color colour)
        {
            var text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.text = value;
            text.color = colour;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        Image ButtonVisual(Transform parent, string name, string value, float x, float y, float w, float h)
        {
            var background = Box(parent, name, x, y, w, h, Ink);
            Label(background.transform, "Label", value, 0, 0, w, h, 19, Color.white);
            return background;
        }

        internal void CloseGrid()
        {
            open = false;
            if (Active == this) Active = null;
            if (portraits != null) { portraits.Dispose(); portraits = null; }
            if (canvasObject != null) { canvasObject.SetActive(false); Object.Destroy(canvasObject); canvasObject = null; }
            if (ownsFont && font != null) Object.Destroy(font);
            ownsFont = false;
            font = null;
            cursors.Clear();
            focused = null;
        }

        void OnDisable() { CloseGrid(); }
        void OnDestroy() { CloseGrid(); }

        internal bool Owns(FrontendChefCustomisation view)
        {
            return owner != null && Array.IndexOf(owner.m_PlayerChefs, view) >= 0;
        }
    }

    [HarmonyPatch]
    internal static class ChefGridPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(FrontendChefMenu), "Show")]
        static void Show(FrontendChefMenu __instance, bool __result)
        {
            if (__result) ChefGridMenu.Show(__instance);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(FrontendChefMenu), "Hide")]
        static void Hide(FrontendChefMenu __instance, bool __result)
        {
            var grid = __instance.GetComponent<ChefGridMenu>();
            if (__result && grid != null) grid.CloseGrid();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(FrontendChefMenu), "Update")]
        static bool Update(FrontendChefMenu __instance)
        {
            var grid = __instance.GetComponent<ChefGridMenu>();
            if (grid == null || !grid.IsOpen) return true;
            grid.Tick();
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(FrontendChefCustomisation), "Update")]
        static bool UpdateChef(FrontendChefCustomisation __instance)
        {
            // Other uses of chef customisation (lobby and DLC auto-selection) keep their normal behavior.
            var menu = ChefGridMenu.Active;
            return menu == null || !menu.IsOpen || !menu.Owns(__instance);
        }
    }
}
