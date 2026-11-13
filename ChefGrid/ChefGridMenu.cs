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
        static ConfigEntry<float> portraitYaw, unselectedDimOpacity;
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
            internal bool InputObserved;
            internal FrontendChefCustomisation View;
            internal PlayerGameInput Input;
            internal readonly ILogicalButton[] Buttons = new ILogicalButton[6];
            internal ChefGridControls Controls = new ChefGridControls();
        }

        sealed class Cell
        {
            internal GameObject Root;
            internal Image[] Highlight;
            internal Image Shade;
            internal RawImage Portrait;
            internal bool Selected;
        }

        BaseMenuBehaviour owner;
        FrontendChefCustomisation[] playerViews;
        Action cancel;
        List<ChefGridEntry> entries;
        readonly List<Cursor> cursors = new List<Cursor>();
        readonly HashSet<long> alive = new HashSet<long>();
        readonly List<ChefGridEntry> wanted = new List<ChefGridEntry>();
        readonly List<long> playerOrder = new List<long>();
        ChefGridTurnOrder turns;
        ChefGridLayout layout;
        Cell[] cells;
        const int Columns = ChefGridLayout.Columns, VisibleCount = ChefGridLayout.VisibleCount;
        readonly Text[] playerLabels = new Text[4];
        readonly Image[] playerTabs = new Image[4];
        GameObject canvasObject;
        RectTransform frame;
        RectTransform gridLayer;
        Font font;
        Text rangeLabel, detailName, detailKind, detailPosition, detailStatus, acceptLabel, acceptKey, backKey;
        RawImage detailPortrait;
        Image acceptIcon, backIcon;
        readonly ChefGridButtonPrompts buttonPrompts = new ChefGridButtonPrompts();
        readonly ChefGridDoubleClick doubleClick = new ChefGridDoubleClick();
        readonly ChefGridScrollBar scrollBar = new ChefGridScrollBar();
        RectTransform scrollThumb;
        float detailReadyAt;
        ChefGridPortraits portraits;
        Cursor focused;
        int firstVisibleRow, openedFrame;
        enum ExitRequest { None, Accept, Cancel }
        ExitRequest exitRequest;
        bool open, failed, ownsFont;
        bool Chinese { get { return Localization.GetLanguage() == SupportedLanguages.Chinese; } }
        string L(string zh, string en) { return Chinese ? zh : en; }
        internal bool IsOpen { get { return open; } }
        internal static bool Enabled { get { return configured != null && configured.Value; } }

        internal static void Configure(ConfigFile config)
        {
            configured = config.Bind("ChefSelection", "GridEnabled", true,
                "Show a scrolling 10-column, 5-row chef grid. Disable to use the original carousel.");
            unselectedDimOpacity = config.Bind("ChefSelection", "UnselectedDimOpacity", 0.4f,
                new ConfigDescription("Black overlay opacity for unselected chefs: 0 disables dimming, 1 is fully dark. 未选中厨师的压暗比例。",
                    new AcceptableValueRange<float>(0f, 1f)));
            portraitYaw = config.Bind("ChefSelection", "PortraitYaw", 0f,
                "Rotation in degrees of generated portraits; changing this rebuilds their visual cache.");
        }

        internal static void Show(FrontendChefMenu menu)
        {
            Show(menu, menu.m_PlayerChefs, menu.Close);
        }

        internal static void Show(BaseMenuBehaviour menu, FrontendChefCustomisation[] views, Action onCancel)
        {
            if (!Enabled) return;
            var grid = menu.GetComponent<ChefGridMenu>() ?? menu.gameObject.AddComponent<ChefGridMenu>();
            if (grid.failed) return;
            try { grid.Open(menu, views, onCancel); }
            catch (Exception ex)
            {
                grid.failed = true;
                grid.CloseGrid();
                DIYChefPlugin.Warn("Chef grid unavailable; original menu retained: " + ex);
            }
        }

        void Open(BaseMenuBehaviour menu, FrontendChefCustomisation[] views, Action onCancel)
        {
            CloseGrid();
            owner = menu;
            playerViews = views;
            cancel = onCancel;
            layout = new ChefGridLayout();
            cells = new Cell[VisibleCount];
            turns = new ChefGridTurnOrder();
            firstVisibleRow = 0;
            exitRequest = ExitRequest.None;
            var progress = GameUtils.GetMetaGameProgress();
            if (progress == null) return;
            entries = ChefGridRoster.Build(GameUtils.GetAvatarDirectoryData().Avatars, progress.GetUnlockedAvatars());
            if (entries.Count == 0) return;
            SyncPlayers();
            if (focused == null) return;
            BuildUI();
            try { portraits = new ChefGridPortraits(focused.View, portraitYaw.Value); }
            catch (Exception ex) { DIYChefPlugin.Warn("Portrait studio unavailable; chef names remain selectable: " + ex); }
            openedFrame = Time.frameCount;
            open = true;
            Active = this;
            ChefGridEngagementGuard.Open();
            // Observe the menu-opening confirm/mouse input without accepting it again.
            PollControls(false);
            PreparePortraits();
            Refresh();
            DIYChefPlugin.Log("Chef grid opened entries=" + entries.Count + " layout=10x5 rows=" +
                ChefGridNavigation.RowCount(entries.Count) + " sequentialPlayers=" + cursors.Count);
        }

        void SyncPlayers()
        {
            var users = ClientUserSystem.m_Users;
            alive.Clear();
            for (int i = 0; i < users.Count && i < playerViews.Length && i < 4; i++)
            {
                var user = users._items[i];
                var view = playerViews[i];
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
            cursors.Sort((a, b) => a.Player.CompareTo(b.Player));
            playerOrder.Clear();
            foreach (var cursor in cursors) playerOrder.Add(cursor.Key);
            turns.Sync(playerOrder);
            if (turns.Current.HasValue)
            {
                if (exitRequest == ExitRequest.Accept) exitRequest = ExitRequest.None;
                Focus(cursors.Find(x => x.Key == turns.Current.Value));
            }
            else if (focused == null || !cursors.Contains(focused))
                focused = cursors.Count == 0 ? null : cursors[cursors.Count - 1];
            // A waiting player may leave after the others confirmed.
            if (turns.Complete && exitRequest == ExitRequest.None) RequestExit(ExitRequest.Accept);
        }

        void Focus(Cursor cursor)
        {
            if (focused == cursor) return;
            doubleClick.Reset();
            scrollBar.End();
            focused = cursor;
            if (focused != null) firstVisibleRow = ChefGridNavigation.Follow(firstVisibleRow, focused.Index, entries.Count);
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
            bool enabled = Time.frameCount > openedFrame && Application.isFocused &&
                !T17DialogBoxManager.HasAnyOpenDialogs() && exitRequest == ExitRequest.None;
            var input = PollControls(enabled);
            if (!enabled) doubleClick.Reset();
            if (exitRequest != ExitRequest.None)
            {
                FinishExit();
                if (!open) return;
                PreparePortraits();
                Refresh();
                return;
            }
            if (enabled)
            {
                if (input.Cancel) RequestExit(ExitRequest.Cancel);
                else
                {
                    if (input.Direction >= 0)
                    {
                        int dx = input.Direction == 0 ? -1 : input.Direction == 1 ? 1 : 0;
                        int dy = input.Direction == 2 ? -1 : input.Direction == 3 ? 1 : 0;
                        Select(focused, ChefGridNavigation.Move(focused.Index, entries.Count, Columns, dx, dy));
                    }
                    if (input.Accept) Accept();
                    else
                    {
                        if (KeyboardUtils.IsKeyboard(focused.Input))
                        {
                            if (Input.GetKeyDown(KeyCode.PageUp)) ShiftRows(-3);
                            if (Input.GetKeyDown(KeyCode.PageDown)) ShiftRows(3);
                        }
                        Mouse();
                    }
                }
            }
            if (!open) return;
            if (exitRequest != ExitRequest.None) FinishExit();
            if (!open) return;
            PreparePortraits();
            Refresh();
        }

        ChefGridControlEvents PollControls(bool enabled)
        {
            var activeEvents = new ChefGridControlEvents { Direction = -1 };
            // Sample every participant before advancing a turn, including shared keyboard keys.
            foreach (var cursor in cursors)
            {
                int direction = -1;
                for (int n = 0; n < 4; n++) if (Down(cursor.Buttons[n])) { direction = n; break; }
                bool keyboard = KeyboardUtils.IsKeyboard(cursor.Input);
                if (keyboard)
                {
                    if (Input.GetKey(KeyCode.LeftArrow)) direction = 0;
                    else if (Input.GetKey(KeyCode.RightArrow)) direction = 1;
                    else if (Input.GetKey(KeyCode.UpArrow)) direction = 2;
                    else if (Input.GetKey(KeyCode.DownArrow)) direction = 3;
                }
                bool accept = Down(cursor.Buttons[4]) || (keyboard && (Input.GetKey(KeyCode.Space) ||
                    Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)));
                bool cancel = Down(cursor.Buttons[5]) || (keyboard && Input.GetKey(KeyCode.Escape));
                var events = cursor.Controls.Poll(direction, accept, cancel, Time.unscaledTime,
                    enabled && cursor.InputObserved && cursor == focused && turns.Current == cursor.Key);
                cursor.InputObserved = true;
                if (cursor == focused) activeEvents = events;
            }
            foreach (var cursor in cursors) foreach (var button in cursor.Buttons) Claim(button);
            return activeEvents;
        }

        static bool Down(ILogicalButton button) { return button != null && button.IsDown(); }

        void PreparePortraits()
        {
            if (portraits != null && focused != null)
            {
                wanted.Clear();
                wanted.Add(entries[focused.Index]);
                int start = firstVisibleRow * Columns;
                int end = Math.Min(entries.Count, start + VisibleCount);
                for (int i = start; i < end; i++)
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

        bool Select(Cursor cursor, int index, bool follow = true)
        {
            if (cursor == null || turns.Current != cursor.Key || index < 0 || index >= entries.Count) return false;
            scrollBar.End();
            if (follow)
            {
                doubleClick.Reset();
                firstVisibleRow = ChefGridNavigation.Follow(firstVisibleRow, index, entries.Count);
            }
            if (cursor.Index == index && cursor.Chosen) return true;
            if (ChefGridRoster.Select(entries[index], cursor.Player, cursor.View))
            {
                cursor.Index = index;
                cursor.Chosen = true;
                GameUtils.TriggerAudio(GameOneShotAudioTag.UIButtonDown, LayerMask.NameToLayer("Administration"));
                return true;
            }
            return false;
        }

        void Accept()
        {
            doubleClick.Reset();
            if (!Select(focused, focused.Index) || !turns.Confirm(focused.Key)) return;
            DIYChefPlugin.Log("Chef grid confirmed player=" + (focused.Player + 1) + " chef=" + entries[focused.Index].Avatar.HeadName);
            if (turns.Complete) RequestExit(ExitRequest.Accept);
            else Focus(cursors.Find(x => x.Key == turns.Current.Value));
        }

        void RequestExit(ExitRequest request) { exitRequest = request; }

        void FinishExit()
        {
            // Hide rejects requests during the original camera animation. Retain the request
            // and retry once allowed, rather than losing an early confirmation or cancellation.
            var flow = T17FrontendFlow.Instance;
            if (T17DialogBoxManager.HasAnyOpenDialogs()) return;
            if (owner is FrontendChefMenu && (flow == null || flow.IsCameraTransitioning())) return;
            if (exitRequest == ExitRequest.Cancel) cancel();
            else if (exitRequest == ExitRequest.Accept)
            {
                var playerMenu = owner as ChefGridLobbyMenu;
                if (playerMenu != null) playerMenu.Commit();
                else if (owner is InGameCustomisation) ChefGridWorldMap.Commit((InGameCustomisation)owner);
                else owner.Hide();
            }
        }

        void ShiftRows(int delta)
        {
            Select(focused, ChefGridNavigation.ShiftRows(focused.Index, entries.Count, delta));
        }

        void Mouse()
        {
            if (!Input.GetMouseButton(0)) scrollBar.End();
            Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, Input.mousePosition, null, out point)) return;
            float x = point.x + 640, y = 360 - point.y;
            // Retain the drag even if the pointer leaves the narrow track or panel.
            if (scrollBar.Dragging)
            {
                firstVisibleRow = scrollBar.Drag(y, entries.Count);
                return;
            }
            if (x < 0 || x > 1280 || y < 0 || y > 720)
            {
                if (Input.GetMouseButtonDown(0)) doubleClick.Reset();
                return;
            }
            if (x < 944 && Input.mouseScrollDelta.y != 0)
                ShiftRows(Input.mouseScrollDelta.y > 0 ? -1 : 1);
            if (!Input.GetMouseButtonDown(0)) return;
            if (scrollBar.Begin(x, y, firstVisibleRow, entries.Count))
            {
                doubleClick.Reset();
                firstVisibleRow = scrollBar.Drag(y, entries.Count);
                return;
            }
            if (y >= 114 && y < 148)
            {
                doubleClick.Reset();
                foreach (var cursor in cursors)
                    if (x >= 48 + cursor.Player * 164 && x < 204 + cursor.Player * 164)
                    {
                        if (turns.Reopen(cursor.Key)) Focus(cursors.Find(c => c.Key == turns.Current.Value));
                        return;
                    }
            }
            int hit = layout.HitTest(x, y, firstVisibleRow, entries.Count);
            if (hit >= 0)
            {
                // Mouse selection keeps the card under the pointer so the second click
                // still targets the same chef, including in the first/last visible row.
                if (Select(focused, hit, false))
                {
                    if (doubleClick.Click(focused.Key, hit, x, y, Time.unscaledTime)) Accept();
                }
                else doubleClick.Reset();
                return;
            }
            doubleClick.Reset();
            if (y >= 650 && y < 686)
            {
                if (x >= 904 && x < 1056) RequestExit(ExitRequest.Cancel);
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
            for (int i = 0; i < VisibleCount; i++)
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
            float scrollLeft = ChefGridScrollBar.CentreX - ChefGridScrollBar.Width / 2;
            Box(frame, "Scroll track", scrollLeft, ChefGridScrollBar.Top, ChefGridScrollBar.Width,
                ChefGridScrollBar.Height, new Color(Ink.r, Ink.g, Ink.b, 0.15f));
            scrollThumb = (RectTransform)Box(frame, "Scroll thumb", scrollLeft, ChefGridScrollBar.Top,
                ChefGridScrollBar.Width, ChefGridScrollBar.Height, Ink).transform;
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
            rangeLabel = Label(frame, "Visible rows", "", 48, 608, 888, 32, 19, Ink);
            Label(frame, "Help", L("方向键 / 摇杆移动　滚轮 / 拖动滚动条　双击厨师确认", "Arrows / stick: move   Wheel / drag bar: scroll   Double-click: confirm"), 48, 650, 840, 36, 18, Ink);
            Text backLabel;
            ActionButton("Back", 904, 152, out backLabel, out backIcon, out backKey);
            backLabel.text = L("返回", "BACK");
            ActionButton("Accept", 1072, 160, out acceptLabel, out acceptIcon, out acceptKey);
        }

        void Refresh()
        {
            for (int i = 0; i < 4; i++)
            {
                var cursor = cursors.Find(x => x.Player == i);
                playerTabs[i].gameObject.SetActive(cursor != null);
                if (cursor == null) continue;
                bool confirmed = turns.IsConfirmed(cursor.Key);
                playerTabs[i].color = cursor == focused && !confirmed ? PlayerColours[i] : Ink;
                playerLabels[i].text = "P" + (i + 1) + (confirmed ? L("  已确认", "  READY") :
                    cursor == focused ? L("  正在选择", "  SELECTING") : L("  等待中", "  WAITING"));
            }
            for (int slot = 0; slot < VisibleCount; slot++)
            {
                int index = firstVisibleRow * Columns + slot;
                var cell = cells[slot];
                cell.Root.SetActive(index < entries.Count);
                if (index >= entries.Count)
                {
                    cell.Selected = false;
                    continue;
                }
                var entry = entries[index];
                var texture = portraits == null ? null : portraits.Get(entry);
                cell.Portrait.texture = texture;
                cell.Portrait.enabled = texture != null;
                // Only the current turn owns a selection frame. Completed and waiting
                // players remain in the status strip, even when they share this chef.
                cell.Selected = focused.Index == index;
                cell.Shade.color = cell.Selected ? new Color(1, 1, 1, 0.06f) :
                    new Color(0, 0, 0, Mathf.Clamp01(unselectedDimOpacity.Value));
                for (int edge = 0; edge < cell.Highlight.Length; edge++)
                {
                    cell.Highlight[edge].enabled = cell.Selected;
                    cell.Highlight[edge].color = PlayerColours[focused.Player];
                }
            }
            // Lift the focused thumbnail with its exterior frame above neighboring cells.
            int selectedSlot = focused.Index - firstVisibleRow * Columns;
            if (selectedSlot >= 0 && selectedSlot < VisibleCount)
            {
                var selectedRoot = cells[selectedSlot].Root.transform;
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
            int rows = ChefGridNavigation.RowCount(entries.Count);
            rangeLabel.text = L("第 ", "ROWS ") + (firstVisibleRow + 1) + "–" + Math.Min(rows, firstVisibleRow + ChefGridLayout.Rows) +
                " / " + rows + L(" 行　·　依次确认厨师", "   ·   CONFIRM IN PLAYER ORDER");
            float thumbHeight = scrollBar.ThumbHeight(entries.Count);
            scrollThumb.sizeDelta = new Vector2(ChefGridScrollBar.Width, thumbHeight);
            scrollThumb.anchoredPosition = new Vector2(ChefGridScrollBar.CentreX,
                -scrollBar.ThumbTop(firstVisibleRow, entries.Count) - thumbHeight / 2);
            scrollThumb.GetComponent<Image>().color = scrollBar.Dragging ? PlayerColours[focused.Player] : Ink;
            acceptLabel.text = exitRequest != ExitRequest.None ? L("返回中…", "FINISHING…") :
                L("确认 P", "CONFIRM P") + (focused.Player + 1);
            buttonPrompts.Refresh(focused.Input, focused.Buttons[4], focused.Buttons[5]);
            ShowPrompt(acceptIcon, acceptKey, buttonPrompts.Accept);
            ShowPrompt(backIcon, backKey, buttonPrompts.Back);
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

        void ActionButton(string name, float x, float width, out Text label, out Image icon, out Text key)
        {
            var background = Box(frame, name, x, 650, width, 36, Ink);
            label = Label(background.transform, "Label", "", 6, 0, width - 66, 36, 19, Color.white);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = 19;
            key = Label(background.transform, "Key", "", width - 56, 0, 50, 36, 17, Color.white);
            key.resizeTextForBestFit = true;
            key.resizeTextMinSize = 12;
            key.resizeTextMaxSize = 17;
            icon = Rect(background.transform, "Key icon", width - 44, 4, 28, 28).gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        static void ShowPrompt(Image icon, Text key, ChefGridButtonPrompt prompt)
        {
            icon.sprite = prompt.Icon;
            icon.enabled = prompt.Icon != null;
            key.text = prompt.Key;
            key.enabled = prompt.Icon == null;
        }

        internal void CloseGrid()
        {
            doubleClick.Reset();
            buttonPrompts.Reset();
            scrollBar.End();
            open = false;
            if (Active == this)
            {
                ChefGridEngagementGuard.Close();
                Active = null;
            }
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
            return owner != null && playerViews != null && Array.IndexOf(playerViews, view) >= 0;
        }
    }

    [HarmonyPatch]
    internal static class ChefGridPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(GamepadEngagementManager), "Update")]
        static void GuardEngagement(GamepadEngagementManager __instance, ILogicalButton[] ___m_cancelButtons)
        {
            ChefGridEngagementGuard.BeforeUpdate(__instance, ___m_cancelButtons);
        }

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
