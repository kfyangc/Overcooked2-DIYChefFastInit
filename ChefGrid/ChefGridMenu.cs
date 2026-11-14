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
            internal ChefGridControlEvents Events;
            internal ChefGridPanelLayout Layout;
            internal int FirstRow;
            internal Panel UI;
        }

        sealed class Cell
        {
            internal GameObject Root;
            internal Image Background;
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
        readonly ChefGridReadySet ready = new ChefGridReadySet();
        sealed class Panel
        {
            internal Cell[] Cells;
            internal RectTransform Grid, Scroll;
            internal Text Name, Kind, Position, Status, Accept, Key, Player;
            internal Image Icon;
            internal RawImage Detail;
            internal float ReadyAt;
            internal readonly ChefGridButtonPrompts Prompts = new ChefGridButtonPrompts();
        }
        GameObject canvasObject;
        RectTransform frame;
        Font font;
        Image backIcon;
        Text backKey;
        readonly ChefGridButtonPrompts backPrompts = new ChefGridButtonPrompts();
        readonly ChefGridControls backControls = new ChefGridControls();
        PlayerGameInput backInput;
        ILogicalButton backButton;
        long? backIdentity;
        bool backObserved;
        Vector3 lastMouse;
        string panelSignature;
        ChefGridPortraits portraits;
        Cursor focused;
        int openedFrame;
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
                "Show independent horizontal player panels with scrolling chef grids. Disable to use the original carousel.");
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
            ready.Sync(new long[0]);
            panelSignature = null;
            lastMouse = Input.mousePosition;
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
            DIYChefPlugin.Log("Chef grid opened entries=" + entries.Count + " layout=horizontal-panels parallelPlayers=" + cursors.Count);
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
                    if (cursor.Controls.Bind((int)input.Pad, (int)input.Side))
                        cursor.InputObserved = false;
                    var ids = new[] { PlayerInputLookup.LogicalButtonID.UILeftPlayerSpecific,
                        PlayerInputLookup.LogicalButtonID.UIRightPlayerSpecific, PlayerInputLookup.LogicalButtonID.UIUp,
                        PlayerInputLookup.LogicalButtonID.UIDown, PlayerInputLookup.LogicalButtonID.UISelect,
                        PlayerInputLookup.LogicalButtonID.UICancel };
                    for (int n = 0; n < ids.Length; n++)
                        cursor.Buttons[n] = PlayerInputLookup.GetFixedButton(ids[n], input);
                    cursor.Buttons[4] = ChefGridButtonPrompts.Binding(input, true);
                    cursor.Buttons[5] = ChefGridButtonPrompts.Binding(input, false);
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
            ready.Sync(playerOrder);
            focused = cursors.Count == 0 ? null : cursors[0];
            string signature = "";
            foreach (var cursor in cursors) signature += cursor.Key + ":" + cursor.Player + ";";
            if (signature != panelSignature)
            {
                panelSignature = signature;
                for (int i = 0; i < cursors.Count; i++)
                {
                    var cursor = cursors[i];
                    cursor.Layout = new ChefGridPanelLayout(i, cursors.Count);
                    cursor.FirstRow = cursor.Layout.Follow(cursor.FirstRow, cursor.Index, entries.Count);
                }
                if (canvasObject != null) { DestroyUI(); BuildUI(); }
            }
            if (!ready.Complete && exitRequest == ExitRequest.Accept) exitRequest = ExitRequest.None;
            if (ready.Complete && exitRequest == ExitRequest.None) RequestExit(ExitRequest.Accept);
            // P1 means the first actual local input identity, even in a scoped P2 arcade menu.
            for (int i = 0; i < users.Count; i++)
            {
                var user = users._items[i];
                if (!user.IsLocal) continue;
                long key = ChefGridNavigation.PlayerKey((int)user.Engagement, (int)user.Split);
                if (backIdentity != key || backInput == null || backInput.Side != user.PadSide)
                {
                    backIdentity = key;
                    var manager = GameUtils.RequireManager<PlayerManager>();
                    backInput = new PlayerGameInput((ControlPadInput.PadNum)user.Engagement, user.PadSide,
                        user.PadSide == PadSide.Both ? manager.UnsidedAmbiMapping : manager.SidedAmbiMapping);
                    backButton = ChefGridButtonPrompts.Binding(backInput, false);
                    backObserved = false;
                }
                break;
            }
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
            bool goBack = PollControls(enabled);
            if (exitRequest != ExitRequest.None)
            {
                FinishExit();
                if (open) { PreparePortraits(); Refresh(); }
                return;
            }
            if (enabled)
            {
                if (goBack) RequestExit(ExitRequest.Cancel);
                else
                {
                    foreach (var cursor in cursors)
                    {
                        var input = cursor.Events;
                        if (!ready.IsConfirmed(cursor.Key) && input.Direction >= 0)
                        {
                            int dx = input.Direction == 0 ? -1 : input.Direction == 1 ? 1 : 0;
                            int dy = input.Direction == 2 ? -1 : input.Direction == 3 ? 1 : 0;
                            Select(cursor, ChefGridNavigation.Move(cursor.Index, entries.Count, cursor.Layout.Columns, dx, dy));
                        }
                        if (!ready.IsConfirmed(cursor.Key) && KeyboardUtils.IsKeyboard(cursor.Input) && cursor.Input.Side == PadSide.Both)
                        {
                            int rows = Input.GetKeyDown(KeyCode.PageUp) ? -3 : Input.GetKeyDown(KeyCode.PageDown) ? 3 : 0;
                            if (rows != 0) Select(cursor, Math.Max(0, Math.Min(entries.Count - 1, cursor.Index + rows * cursor.Layout.Columns)));
                        }
                        if (input.Accept) Accept(cursor);
                    }
                    if (exitRequest == ExitRequest.None) Mouse();
                }
            }
            if (!open) return;
            if (exitRequest != ExitRequest.None) FinishExit();
            if (!open) return;
            PreparePortraits();
            Refresh();
        }

        bool PollControls(bool enabled)
        {

            // Sample every participant before Select can publish a usersChanged event.
            foreach (var cursor in cursors)
            {
                int direction = -1;
                for (int n = 0; n < 4; n++) if (Down(cursor.Buttons[n])) { direction = n; break; }
                bool keyboard = KeyboardUtils.IsKeyboard(cursor.Input) && cursor.Input.Side == PadSide.Both;
                if (keyboard)
                {
                    if (Input.GetKey(KeyCode.LeftArrow)) direction = 0;
                    else if (Input.GetKey(KeyCode.RightArrow)) direction = 1;
                    else if (Input.GetKey(KeyCode.UpArrow)) direction = 2;
                    else if (Input.GetKey(KeyCode.DownArrow)) direction = 3;
                }
                bool accept = ChefGridButtonPrompts.Down(cursor.Input, cursor.Buttons[4]) || (keyboard && (Input.GetKey(KeyCode.Space) ||
                    Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)));
                bool cancel = ChefGridButtonPrompts.Down(cursor.Input, cursor.Buttons[5]) || (keyboard && Input.GetKey(KeyCode.Escape));
                cursor.Events = cursor.Controls.Poll(direction, accept, cancel, Time.unscaledTime,
                    enabled && cursor.InputObserved);
                cursor.InputObserved = true;

            }
            foreach (var cursor in cursors) foreach (var button in cursor.Buttons) Claim(button);
            bool keyboardBack = backInput != null && KeyboardUtils.IsKeyboard(backInput) && backInput.Side == PadSide.Both;
            var back = backControls.Poll(-1, false, ChefGridButtonPrompts.Down(backInput, backButton) || (keyboardBack && Input.GetKey(KeyCode.Escape)),
                Time.unscaledTime, enabled && backObserved);
            backObserved = true;
            Claim(backButton);
            return back.Cancel;
        }

        static bool Down(ILogicalButton button) { return button != null && button.IsDown(); }

        void PreparePortraits()
        {
            if (portraits != null && focused != null)
            {
                wanted.Clear();
                var selected = new List<ChefGridEntry>();
                foreach (var cursor in cursors)
                    if (!selected.Contains(entries[cursor.Index])) selected.Add(entries[cursor.Index]);
                wanted.AddRange(selected);
                foreach (var cursor in cursors)
                {
                    int start = cursor.FirstRow * cursor.Layout.Columns;
                    for (int i = start; i < Math.Min(entries.Count, start + cursor.Layout.VisibleCount); i++)
                        if (!wanted.Contains(entries[i])) wanted.Add(entries[i]);
                }
                portraits.Tick(wanted, selected);
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
            if (cursor == null || ready.IsConfirmed(cursor.Key) || index < 0 || index >= entries.Count) return false;
            cursor.Layout.EndScroll();
            if (follow) cursor.FirstRow = cursor.Layout.Follow(cursor.FirstRow, index, entries.Count);
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

        void Accept(Cursor cursor)
        {
            if (ready.IsConfirmed(cursor.Key)) { ready.Reopen(cursor.Key); return; }
            if (!Select(cursor, cursor.Index) || !ready.Confirm(cursor.Key)) return;
            if (ready.Complete) RequestExit(ExitRequest.Accept);
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

        void Mouse()
        {
            Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, Input.mousePosition, null, out point)) return;
            float x = point.x + 640, y = 360 - point.y;
            bool moved = (Input.mousePosition - lastMouse).sqrMagnitude > 0.1f;
            lastMouse = Input.mousePosition;
            bool click = Input.GetMouseButtonDown(0);
            // A drag belongs to its original panel even while crossing another one.
            foreach (var cursor in cursors)
            {
                var layout = cursor.Layout;
                if (layout.Dragging)
                {
                    if (Input.GetMouseButton(0)) cursor.FirstRow = layout.DragScroll(y, entries.Count);
                    else layout.EndScroll();
                    return;
                }
            }
            foreach (var cursor in cursors)
            {
                var layout = cursor.Layout;
                if (x < layout.Left || x >= layout.Left + layout.Width) continue;
                if (click && x >= 48 && x < 188 && y >= 650 && y < 686) { RequestExit(ExitRequest.Cancel); return; }
                if (click && x >= layout.Left + layout.Width - 140 && y >= 650 && y < 686) { Accept(cursor); return; }
                if (click && layout.BeginScroll(x, y, cursor.FirstRow, entries.Count))
                { cursor.FirstRow = layout.DragScroll(y, entries.Count); return; }
                if (Input.mouseScrollDelta.y != 0 && y >= layout.GridTop && y < layout.GridTop + layout.GridHeight)
                {
                    cursor.FirstRow = Math.Max(0, Math.Min(layout.LastFirstRow(entries.Count),
                        cursor.FirstRow + (Input.mouseScrollDelta.y > 0 ? -1 : 1)));
                    return;
                }
                int hit = layout.HitTest(x, y, cursor.FirstRow, entries.Count);
                if (hit >= 0 && (moved || click) && Select(cursor, hit, false) && click) Accept(cursor);
                return;
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
            foreach (var cursor in cursors)
            {
                var l = cursor.Layout;
                var ui = cursor.UI = new Panel();
                Box(frame, "Player panel", l.Left, 104, l.Width, 584, PlayerColours[cursor.Player]);
                Box(frame, "Player paper", l.Left + 3, 107, l.Width - 6, 578, Paper);
                ui.Player = Label(frame, "Player", "P" + (cursor.Player + 1), l.Left + 10, 108, l.Width - 20, 28, 21, Ink);
                float imageSize = l.DetailImageSize, textX = l.DetailLeft + imageSize + 8, detailY = l.DetailTop;
                ui.Detail = Rect(frame, "Selected portrait", l.DetailLeft, detailY, imageSize, imageSize).gameObject.AddComponent<RawImage>();
                ui.Detail.raycastTarget = false;
                ui.Name = Label(frame, "Chef name", "", textX, l.DetailTextTop, l.DetailTextWidth, 66, 22, Ink);
                ui.Name.resizeTextForBestFit = true; ui.Name.resizeTextMinSize = 16; ui.Name.resizeTextMaxSize = 22;
                ui.Kind = Label(frame, "Chef kind", "", textX, l.DetailTextTop + 66, l.DetailTextWidth, 28, 17, Ink);
                ui.Position = Label(frame, "Position", "", textX, l.DetailTextTop + 94, l.DetailTextWidth, 28, 17, Ink);
                ui.Status = Label(frame, "Preview status", "", l.DetailLeft, detailY + (imageSize - 48) / 2, imageSize, 48, 16, Ink);
                ui.Grid = Rect(frame, "Grid", 0, 0, 1280, 720);
                ui.Cells = new Cell[l.VisibleCount];
                for (int slot = 0; slot < ui.Cells.Length; slot++)
                {
                    var rect = l.Cell(slot); float side = rect.Size;
                    var cell = ui.Cells[slot] = new Cell();
                    cell.Background = Box(ui.Grid, "Chef", rect.X, rect.Y, side, side, Paper);
                    cell.Root = cell.Background.gameObject;
                    cell.Portrait = Rect(cell.Root.transform, "Portrait", 0, 0, side, side).gameObject.AddComponent<RawImage>();
                    cell.Portrait.raycastTarget = false;
                    cell.Shade = Box(cell.Root.transform, "Shade", 0, 0, side, side, Color.clear);
                    const float stroke = 5;
                    cell.Highlight = new[] {
                        Box(cell.Root.transform, "Selected top", -stroke, -stroke, side + stroke * 2, stroke, Ink),
                        Box(cell.Root.transform, "Selected right", side, 0, stroke, side, Ink),
                        Box(cell.Root.transform, "Selected bottom", -stroke, side, side + stroke * 2, stroke, Ink),
                        Box(cell.Root.transform, "Selected left", -stroke, 0, stroke, side, Ink) };
                }
                float trackX = l.ScrollLeft;
                Box(frame, "Scroll track", trackX, l.GridTop, 6, l.GridHeight, new Color(Ink.r, Ink.g, Ink.b, 0.15f));
                ui.Scroll = (RectTransform)Box(frame, "Scroll thumb", trackX, l.GridTop, 6, l.GridHeight, Ink).transform;
                ActionButton("Confirm", l.Left + l.Width - 140, 140, out ui.Accept, out ui.Icon, out ui.Key);
            }
            Text backLabel;
            ActionButton("Back", 48, 140, out backLabel, out backIcon, out backKey);
            backLabel.text = L("返回 P1", "BACK P1");
        }

        void Refresh()
        {
            foreach (var cursor in cursors)
            {
                var ui = cursor.UI; var l = cursor.Layout;
                bool confirmed = ready.IsConfirmed(cursor.Key);
                ui.Player.text = "P" + (cursor.Player + 1) + (confirmed ? L(" 已确认", " READY") : L(" 选择中", " SELECTING"));
                for (int slot = 0; slot < ui.Cells.Length; slot++)
                {
                    var cell = ui.Cells[slot]; int index = cursor.FirstRow * l.Columns + slot;
                    cell.Root.SetActive(index < entries.Count);
                    if (index >= entries.Count) continue;
                    var texture = portraits == null ? null : portraits.Get(entries[index]);
                    cell.Portrait.texture = texture; cell.Portrait.enabled = texture != null;
                    cell.Selected = index == cursor.Index;
                    cell.Background.color = cell.Selected ? PlayerColours[cursor.Player] : Paper;
                    cell.Shade.color = cell.Selected ? Color.clear : new Color(0, 0, 0, Mathf.Clamp01(unselectedDimOpacity.Value));
                    foreach (var edge in cell.Highlight) { edge.enabled = cell.Selected; edge.color = PlayerColours[cursor.Player]; }
                    if (cell.Selected) cell.Root.transform.SetAsLastSibling();
                }
                var selected = entries[cursor.Index];
                var detail = portraits == null ? null : portraits.GetDetail(selected);
                if (detail != ui.Detail.texture) ui.ReadyAt = Time.unscaledTime;
                ui.Detail.texture = detail; ui.Detail.enabled = detail != null;
                if (detail != null) ui.Detail.color = new Color(1, 1, 1, Mathf.Clamp01((Time.unscaledTime - ui.ReadyAt) / 0.15f));
                ui.Status.enabled = detail == null;
                ui.Status.text = portraits != null && portraits.DetailFailed(selected) ? L("预览失败", "FAILED") : L("加载预览…", "LOADING…");
                ui.Name.text = selected.Label;
                ui.Kind.text = selected.CustomID == 255 ? L("官方厨师", "ORIGINAL") : L("额外厨师", "CUSTOM");
                ui.Position.text = (cursor.Index + 1) + " / " + entries.Count;
                float height = l.ThumbHeight(entries.Count);
                ui.Scroll.sizeDelta = new Vector2(6, height);
                ui.Scroll.anchoredPosition = new Vector2(l.ScrollLeft + 3, -l.ThumbTop(cursor.FirstRow, entries.Count) - height / 2);
                ui.Accept.text = confirmed ? L("重新选择", "EDIT") : L("确认", "READY");
                ui.Prompts.Refresh(cursor.Input, cursor.Buttons[4], cursor.Buttons[5]);
                ShowPrompt(ui.Icon, ui.Key, ui.Prompts.Accept);
            }
            backPrompts.Refresh(backInput, null, backButton);
            ShowPrompt(backIcon, backKey, backPrompts.Back);
        }

        void DestroyUI()
        {
            if (canvasObject != null) { canvasObject.SetActive(false); Object.Destroy(canvasObject); canvasObject = null; }
            if (ownsFont && font != null) Object.Destroy(font);
            ownsFont = false; font = null;
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
            backPrompts.Reset();
            backIdentity = null; backObserved = false;
            open = false;
            if (Active == this)
            {
                ChefGridEngagementGuard.Close();
                Active = null;
            }
            if (portraits != null) { portraits.Dispose(); portraits = null; }
            DestroyUI();
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
