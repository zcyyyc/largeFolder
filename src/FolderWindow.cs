using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Markup;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LargeFolder
{
    public sealed class FolderWindow : Window
    {
        public readonly FolderGroup Group;
        private readonly AppController controller;
        private bool expanded, modal, moving, rebuilding, permitClose, draggingItem;
        private bool resizingCard, applyingSize, sizeRefreshQueued, canMoveCard, resizeChanged, suppressHeaderClick;
        private Thumb activeResizeThumb;
        private CardBounds resizeStart;
        private Point resizeStartScreen;
        private double resizeStoredWidth, resizeStoredHeight, resizeMaxWidth, resizeMaxHeight;
        private WrapPanel compactPreview;
        private Button compactAllButton;
        [Flags] private enum ResizeEdges { Left = 1, Top = 2, Right = 4, Bottom = 8 }
        private double anchorX, anchorY;
        private Border card;
        private Grid content;
        private WrapPanel itemsPanel;
        private TextBox search;
        private Border searchShell;
        private TextBlock resultLabel;
        private Canvas dragOverlay;
        private Border dropMarker;
        private ScrollViewer itemsScroll;
        private System.Windows.Threading.DispatcherTimer dragScrollTimer;
        private Point dragViewportPoint;
        private int dragScrollDirection;
        private static ControlTemplate itemButtonTemplate, darkItemButtonTemplate;
        private static readonly string[] PresetColors = { "#EAF3EB", "#E7EFF8", "#FAF0DF", "#EFEAF7", "#F8E9ED" };
        private bool darkCard;
        private Brush cardInk = Ui.Ink, cardText = Ui.Brush("#2F4036"), cardMuted = Ui.Muted;
        private const string ItemDragFormat = "LargeFolder.ItemOrder.v1";
        private sealed class ItemDragData { public string GroupId; public string ItemId; }
        private sealed class DropPosition { public Button Target; public bool After; }
        private Point down;
        private Point moveStartScreen;
        private double moveStartLeft, moveStartTop;
        private const double SmallW = 142, LargeW = 390, LargeH = 460;
        public FolderWindow(AppController controller, FolderGroup group)
        {
            this.controller = controller; Group = group;
            Title = "大文件夹 · " + Group.Name; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            UseLayoutRounding = true; SnapsToDevicePixels = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = Program.InspectMode; Topmost = controller.Data.AlwaysOnTop; Width = SmallW; Height = CompactHeight();
            Left = group.Left; Top = group.Top; anchorX = Left; anchorY = Top;
            AllowDrop = true; DragOver += OnDragOver; Drop += OnDrop;
            DragLeave += delegate { ClearDropIndicator(); };
            PreviewKeyDown += OnKey;
            Deactivated += delegate {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                timer.Tick += delegate { timer.Stop(); if (controller.Data.CloseOnBlur && expanded && !modal && !draggingItem && !resizingCard && !canMoveCard && !IsActive && !HasOpenMenu() && !OwnedWindows.Cast<Window>().Any(w => w.IsActive)) Collapse(); }; timer.Start();
            };
            Loaded += delegate { ApplyModeSize(); EnsureVisible(); anchorX = Left; anchorY = Top; };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (resizingCard && activeResizeThumb != null) activeResizeThumb.CancelDrag(); if (canMoveCard) FinishCardMove(false); if (!permitClose && !controller.IsExiting) { e.Cancel = true; Collapse(); Hide(); } };
            Build();
            SizeChanged += delegate { QueueSizeRefresh(); };
        }
        public void ClosePermanently() { permitClose = true; Close(); }
        private bool HasOpenMenu() { return card != null && card.ContextMenu != null && card.ContextMenu.IsOpen || itemsPanel != null && itemsPanel.Children.OfType<Button>().Any(b => b.ContextMenu != null && b.ContextMenu.IsOpen); }
        public void EnsureVisible()
        {
            if (resizingCard || (canMoveCard && moving)) return;
            Rect area = CurrentWorkArea();
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            if (!expanded) { Group.Left = Left; Group.Top = Top; }
        }
        private Rect CurrentWorkArea()
        {
            var monitors = Forms.Screen.AllScreens;
            var source = PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null) return SystemParameters.WorkArea;
            double scaleX = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            double scaleY = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M22;
            var rect = new System.Drawing.Rectangle((int)(Left * scaleX), (int)(Top * scaleY), (int)(Width * scaleX), (int)(Height * scaleY));
            var screen = monitors.OrderByDescending(s => System.Drawing.Rectangle.Intersect(s.WorkingArea, rect).Width * (long)System.Drawing.Rectangle.Intersect(s.WorkingArea, rect).Height).First();
            var area = screen.WorkingArea;
            return new Rect(area.Left / scaleX, area.Top / scaleY, area.Width / scaleX, area.Height / scaleY);
        }
        public void Expand()
        {
            if (expanded || resizingCard) return;
            anchorX = Left; anchorY = Top;
            foreach (var w in controller.Windows) if (w != this) w.Collapse();
            expanded = true; ApplyModeSize(); Activate();
        }
        public void Collapse()
        {
            if (!expanded || resizingCard) return;
            expanded = false; Left = anchorX; Top = anchorY; ApplyModeSize(); EnsureVisible(); controller.Save();
        }
        private int CompactColumns(double width) { return CardGeometry.Preview(width, 112, 0).Columns; }
        private int ExpandedColumns() { return Math.Max(1, (int)Math.Floor((Width - 59) / 80)); }
        private double CompactHeight() { int columns = CompactColumns(Width > 0 ? Width : SmallW); int rows = Math.Max(1, (Math.Min(4, Group.Items.Count) + columns - 1) / columns); return 56 + rows * 56 + (Group.Items.Count > columns * rows ? 24 : 0); }
        private double MinimumModeHeight(double width) { return expanded ? (searchShell != null && searchShell.Visibility == Visibility.Visible ? 216 : CardGeometry.MinExpandedHeight) : (Group.Items.Count > CompactColumns(width) ? 136 : CardGeometry.MinCompactHeight); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(value, max)); }
        private void SetModeLimits(double width)
        {
            Rect area = CurrentWorkArea();
            MinWidth = expanded ? CardGeometry.MinExpandedWidth : CardGeometry.MinCompactWidth;
            MinHeight = MinimumModeHeight(width);
            MaxWidth = Math.Max(MinWidth, Math.Min(CardGeometry.MaxWidth, area.Width));
            MaxHeight = Math.Max(MinHeight, Math.Min(CardGeometry.MaxHeight, area.Height));
        }
        private void ApplyModeSize()
        {
            applyingSize = true;
            try {
                double preferredWidth = expanded ? Group.ExpandedWidth : Group.CompactWidth;
                SetModeLimits(preferredWidth > 0 ? preferredWidth : (expanded ? LargeW : SmallW));
                Width = Clamp(preferredWidth > 0 ? preferredWidth : (expanded ? LargeW : SmallW), MinWidth, MaxWidth);
                // Search is closed when rebuilding the view; its minimum is reapplied when opened.
                if (expanded) MinHeight = CardGeometry.MinExpandedHeight;
                else MinHeight = MinimumModeHeight(Width);
                double preferredHeight = expanded ? Group.ExpandedHeight : Group.CompactHeight;
                double autoHeight = expanded ? 82 + (Group.Items.Count == 0 ? 110 : Math.Max(1, (Group.Items.Count + ExpandedColumns() - 1) / ExpandedColumns()) * 86) : CompactHeight();
                Height = Clamp(preferredHeight > 0 ? preferredHeight : Math.Min(expanded ? LargeH : MaxHeight, autoHeight), MinHeight, MaxHeight);
                Build(); EnsureVisible();
            } finally { applyingSize = false; }
        }
        private void QueueSizeRefresh()
        {
            if (!IsLoaded || applyingSize || sizeRefreshQueued) return;
            sizeRefreshQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(delegate {
                sizeRefreshQueued = false;
                if (!IsLoaded || applyingSize) return;
                if (expanded) { if (itemsPanel != null) { itemsPanel.Width = ExpandedColumns() * 80; FitExpandedHeight(); } }
                else BuildCompact();
            }));
        }
        private void FitExpandedHeight()
        {
            if (!expanded || itemsPanel == null) return;
            itemsPanel.Width = ExpandedColumns() * 80;
            int count = itemsPanel.Children.Count;
            double body = Group.Items.Count == 0 || (count == 1 && itemsPanel.Children[0] is StackPanel) ? 110 : Math.Max(1, (count + ExpandedColumns() - 1) / ExpandedColumns()) * 86;
            double tools = searchShell != null && searchShell.Visibility == Visibility.Visible ? 46 : 0;
            if (!resizingCard && Group.ExpandedHeight <= 0) Height = Clamp(Math.Min(LargeH, 82 + tools + body), MinHeight, MaxHeight);
            EnsureVisible();
        }
        private void ShowSearch() { Expand(); searchShell.Visibility = Visibility.Visible; MinHeight = 216; if (Height < MinHeight) Height = MinHeight; FitExpandedHeight(); search.Focus(); search.SelectAll(); }
        private string Tint() { try { Ui.Brush(Group.Color); return Group.Color; } catch { return "#EAF3EB"; } }
        public void RefreshItems() { if (expanded) FillItems(); else { SetModeLimits(Width); if (Group.CompactHeight <= 0) Height = Clamp(CompactHeight(), MinHeight, MaxHeight); Build(); EnsureVisible(); } }
        private void Build()
        {
            rebuilding = true;
            var shell = new Grid { Margin = new Thickness(6) };
            var tint = Ui.Brush(Tint()).Color;
            bool customTint = !PresetColors.Contains(Tint(), StringComparer.OrdinalIgnoreCase);
            darkCard = customTint && tint.R * 0.299 + tint.G * 0.587 + tint.B * 0.114 < 135;
            cardInk = darkCard ? Brushes.White : Ui.Ink;
            cardText = darkCard ? Brushes.White : Ui.Brush("#2F4036");
            cardMuted = darkCard ? Ui.Brush("#D7E1DC") : Ui.Muted;
            Color topTint = customTint ? Color.FromRgb((byte)((tint.R * 7 + 255) / 8), (byte)((tint.G * 7 + 255) / 8), (byte)((tint.B * 7 + 255) / 8)) : Color.FromRgb((byte)((tint.R + 765) / 4), (byte)((tint.G + 765) / 4), (byte)((tint.B + 765) / 4));
            Color bottomTint = customTint ? tint : Color.FromRgb((byte)((tint.R + 255) / 2), (byte)((tint.G + 255) / 2), (byte)((tint.B + 255) / 2));
            var restingBackground = new LinearGradientBrush(topTint, bottomTint, new Point(0, 0), new Point(0, 1));
            restingBackground.Opacity = 0.72;
            card = new Border { CornerRadius = new CornerRadius(15), Background = restingBackground, BorderBrush = Ui.Brush("#A6DEE6E1"), BorderThickness = new Thickness(1), Padding = new Thickness(expanded ? 14 : 8), Cursor = Cursors.SizeAll, Effect = new DropShadowEffect { Color = Color.FromRgb(35, 56, 47), BlurRadius = 8, ShadowDepth = 1, Opacity = 0.12 } };
            card.ContextMenu = GroupMenu(); shell.Children.Add(card); Content = shell;
            dragOverlay = new Canvas { IsHitTestVisible = false };
            dropMarker = new Border { Width = 3, Height = 70, CornerRadius = new CornerRadius(1.5), Background = Ui.Accent, Visibility = Visibility.Collapsed };
            dragOverlay.Children.Add(dropMarker); shell.Children.Add(dragOverlay);
            content = new Grid(); card.Child = content;
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new Grid { Margin = new Thickness(0, 0, 0, expanded ? 10 : 4), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
            if (!expanded) header.Height = 22;
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleStack = new StackPanel();
            var title = Ui.Text(Group.Name, expanded ? 17 : 11.5, cardInk); title.FontWeight = FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = Group.Name + " · " + Group.Items.Count + " 个项目" + "\n拖动标题或空白处移动，拖动边框调整大小" + (expanded ? "" : "\n单击标题展开");
            titleStack.Children.Add(title); header.Children.Add(titleStack);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (expanded) { var add = Ui.Button("+", ShowAddMenu, false); add.ToolTip = "整理桌面或添加入口"; add.FontSize = 17; add.Padding = new Thickness(7, 0, 7, 2); add.Background = Brushes.Transparent; add.Foreground = cardInk; actions.Children.Add(add); }
            var more = Ui.Button("···", delegate { card.ContextMenu.IsOpen = true; }, false); more.ToolTip = "分组菜单（也可右键）"; more.Padding = new Thickness(5, 0, 5, 2); more.FontSize = 16; more.Background = Brushes.Transparent; more.Foreground = expanded ? cardInk : Ui.Ink; more.Opacity = expanded ? 1 : 0;
            if (expanded) actions.Children.Add(more);
            if (!expanded) {
                more.Width = 18; more.Height = 18; more.Padding = new Thickness(0); more.FontSize = 14; more.HorizontalAlignment = HorizontalAlignment.Right; more.VerticalAlignment = VerticalAlignment.Center; more.Background = Ui.Brush("#F5F8F5"); Panel.SetZIndex(more, 1); header.Children.Add(more);
                var expand = Ui.Button("", Expand, false); expand.ToolTip = "展开文件夹，查看全部项目"; expand.Width = 22; expand.Height = 22; expand.Padding = new Thickness(0); expand.Margin = new Thickness(3, 0, 0, 0); expand.Background = Ui.Brush("#EDF2EE");
                expand.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 2,10 L 10,2 M 3,2 L 10,2 L 10,9"), Stroke = Ui.Brush("#607467"), StrokeThickness = 1.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 12, Height = 12, Stretch = Stretch.Uniform }; actions.Children.Add(expand);
                System.Windows.Automation.AutomationProperties.SetName(expand, "展开文件夹");
                var compactCard = card;
                compactCard.MouseEnter += delegate { more.Opacity = 1; };
                compactCard.MouseLeave += delegate { if (!more.IsKeyboardFocused) more.Opacity = 0; };
                more.GotKeyboardFocus += delegate { more.Opacity = 1; };
                more.LostKeyboardFocus += delegate { if (!card.IsMouseOver) more.Opacity = 0; };
            }
            if (expanded) { var close = Ui.Button("×", Collapse, false); close.ToolTip = "收起 (Esc)"; close.FontSize = 17; close.Padding = new Thickness(7, 0, 7, 2); close.Margin = new Thickness(2, 0, 0, 0); close.Background = Brushes.Transparent; close.Foreground = cardInk; actions.Children.Add(close); }
            Grid.SetColumn(actions, 1); header.Children.Add(actions);
            card.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) {
                suppressHeaderClick = false;
                canMoveCard = !IsButton(e.OriginalSource as DependencyObject);
                if (canMoveCard) { down = e.GetPosition(this); moveStartScreen = PointToScreen(down); moveStartLeft = Left; moveStartTop = Top; moving = false; Mouse.Capture(card, CaptureMode.SubTree); }
            };
            card.PreviewMouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { if (canMoveCard) { bool wasMoving = moving; FinishCardMove(false); if (wasMoving) e.Handled = true; } };
            card.LostMouseCapture += delegate { if (canMoveCard && Mouse.Captured != card) { suppressHeaderClick = true; FinishCardMove(false); } };
            card.PreviewMouseMove += delegate(object s, MouseEventArgs e) {
                if (!canMoveCard || e.LeftButton != MouseButtonState.Pressed || resizingCard || draggingItem) return;
                Point screen = PointToScreen(e.GetPosition(this)); var source = PresentationSource.FromVisual(this);
                double sx = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
                double sy = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M22;
                double dx = (screen.X - moveStartScreen.X) / sx, dy = (screen.Y - moveStartScreen.Y) / sy;
                if (!moving && Math.Abs(dx) + Math.Abs(dy) < 7) return;
                moving = true; suppressHeaderClick = true; Left = moveStartLeft + dx; Top = moveStartTop + dy; e.Handled = true;
            };
            header.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (!IsButton(e.OriginalSource as DependencyObject) && !moving && !suppressHeaderClick && !expanded) Expand(); moving = false; };
            content.Children.Add(header);
            if (expanded) BuildExpanded(); else BuildCompact();
            AddResizeGrips(shell);
            rebuilding = false;
        }
        private static bool IsButton(DependencyObject current) { while (current != null) { if (current is Button || current is TextBox || current is Thumb || current is ScrollBar) return true; current = VisualTreeHelper.GetParent(current); } return false; }
        private void FinishCardMove(bool cancel)
        {
            bool changed = moving; canMoveCard = false; moving = false;
            if (Mouse.Captured == card) Mouse.Capture(null);
            if (cancel) { suppressHeaderClick = true; Left = moveStartLeft; Top = moveStartTop; return; }
            if (!changed) return;
            EnsureVisible(); anchorX = Left; anchorY = Top; Group.Left = Left; Group.Top = Top; controller.Save();
        }
        private static ControlTemplate ItemButtonTemplate(bool dark)
        {
            ControlTemplate cached = dark ? darkItemButtonTemplate : itemButtonTemplate;
            if (cached != null) return cached;
            string markup = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Grid><Border Background='{TemplateBinding Background}' CornerRadius='9'/><Border x:Name='hover' Background='#B0FFFFFF' CornerRadius='9' Opacity='0'/><Border x:Name='focus' BorderBrush='Transparent' BorderThickness='1' CornerRadius='9'/><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='hover' Property='Opacity' Value='1'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='hover' Property='Background' Value='#DCE8DF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='focus' Property='BorderBrush' Value='#80337557'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
            if (dark) markup = markup.Replace("#B0FFFFFF", "#20FFFFFF").Replace("#DCE8DF", "#33000000").Replace("#80337557", "#B0B9D6C5");
            var template = (ControlTemplate)XamlReader.Parse(markup);
            if (dark) darkItemButtonTemplate = template; else itemButtonTemplate = template;
            return template;
        }
        private void AddResizeGrips(Grid shell)
        {
            var layer = new Grid(); Panel.SetZIndex(layer, 30); shell.Children.Add(layer);
            AddResizeGrip(layer, ResizeEdges.Left, HorizontalAlignment.Left, VerticalAlignment.Stretch, 6, Double.NaN, new Thickness(0, 12, 0, 12), Cursors.SizeWE, "调整左边大小");
            AddResizeGrip(layer, ResizeEdges.Right, HorizontalAlignment.Right, VerticalAlignment.Stretch, 6, Double.NaN, new Thickness(0, 12, 0, 12), Cursors.SizeWE, "调整右边大小");
            AddResizeGrip(layer, ResizeEdges.Top, HorizontalAlignment.Stretch, VerticalAlignment.Top, Double.NaN, 6, new Thickness(12, 0, 12, 0), Cursors.SizeNS, "调整上边大小");
            AddResizeGrip(layer, ResizeEdges.Bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Double.NaN, 6, new Thickness(12, 0, 12, 0), Cursors.SizeNS, "调整下边大小");
            AddResizeGrip(layer, ResizeEdges.Left | ResizeEdges.Top, HorizontalAlignment.Left, VerticalAlignment.Top, 12, 12, new Thickness(), Cursors.SizeNWSE, "调整左上角大小");
            AddResizeGrip(layer, ResizeEdges.Right | ResizeEdges.Top, HorizontalAlignment.Right, VerticalAlignment.Top, 12, 12, new Thickness(), Cursors.SizeNESW, "调整右上角大小");
            AddResizeGrip(layer, ResizeEdges.Left | ResizeEdges.Bottom, HorizontalAlignment.Left, VerticalAlignment.Bottom, 12, 12, new Thickness(), Cursors.SizeNESW, "调整左下角大小");
            AddResizeGrip(layer, ResizeEdges.Right | ResizeEdges.Bottom, HorizontalAlignment.Right, VerticalAlignment.Bottom, 12, 12, new Thickness(), Cursors.SizeNWSE, "调整右下角大小");
        }
        private void AddResizeGrip(Grid layer, ResizeEdges edges, HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height, Thickness margin, Cursor cursor, string name)
        {
            var grip = new Thumb { Width = width, Height = height, Margin = margin, HorizontalAlignment = horizontal, VerticalAlignment = vertical, Cursor = cursor, Focusable = false, IsTabStop = false, Tag = edges, ToolTip = "拖动边框或角落调整大小" };
            string hint = edges == (ResizeEdges.Right | ResizeEdges.Bottom) ? "<Path Data='M 2,8 L 8,2 M 5,8 L 8,5' Stroke='#96A99C' StrokeThickness='1' Width='8' Height='8' HorizontalAlignment='Right' VerticalAlignment='Bottom' Margin='2'/>" : "";
            grip.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid><Border Background='#01000000'/>" + hint + "</Grid></ControlTemplate>");
            System.Windows.Automation.AutomationProperties.SetName(grip, name);
            grip.DragStarted += StartCardResize;
            grip.DragDelta += ContinueCardResize;
            grip.DragCompleted += FinishCardResize;
            layer.Children.Add(grip);
        }
        private void StartCardResize(object sender, DragStartedEventArgs e)
        {
            if (draggingItem) return;
            SetModeLimits(Width);
            activeResizeThumb = (Thumb)sender; resizingCard = true; canMoveCard = false; resizeChanged = false;
            resizeStart = new CardBounds { Left = Left, Top = Top, Width = Width, Height = Height };
            resizeStartScreen = PointToScreen(Mouse.GetPosition(this));
            resizeStoredWidth = expanded ? Group.ExpandedWidth : Group.CompactWidth;
            resizeStoredHeight = expanded ? Group.ExpandedHeight : Group.CompactHeight;
            resizeMaxWidth = MaxWidth; resizeMaxHeight = MaxHeight;
            ClearDropIndicator();
        }
        private void ContinueCardResize(object sender, DragDeltaEventArgs e)
        {
            if (!resizingCard || activeResizeThumb != sender) return;
            Point screen = PointToScreen(Mouse.GetPosition(this));
            var source = PresentationSource.FromVisual(this);
            double sx = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            double sy = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M22;
            double dx = (screen.X - resizeStartScreen.X) / sx, dy = (screen.Y - resizeStartScreen.Y) / sy;
            var edges = (ResizeEdges)activeResizeThumb.Tag;
            bool left = (edges & ResizeEdges.Left) != 0, top = (edges & ResizeEdges.Top) != 0, right = (edges & ResizeEdges.Right) != 0, bottom = (edges & ResizeEdges.Bottom) != 0;
            var bounds = CardGeometry.Resize(resizeStart.Left, resizeStart.Top, resizeStart.Width, resizeStart.Height, dx, dy, left, top, right, bottom, MinWidth, CardGeometry.MinCompactHeight, resizeMaxWidth, resizeMaxHeight);
            double minimumHeight = MinimumModeHeight(bounds.Width);
            bounds = CardGeometry.Resize(resizeStart.Left, resizeStart.Top, resizeStart.Width, resizeStart.Height, dx, dy, left, top, right, bottom, MinWidth, minimumHeight, resizeMaxWidth, resizeMaxHeight);
            applyingSize = true;
            try { MinHeight = minimumHeight; Width = bounds.Width; Height = bounds.Height; Left = bounds.Left; Top = bounds.Top; }
            finally { applyingSize = false; }
            resizeChanged = Math.Abs(Width - resizeStart.Width) > 0.5 || Math.Abs(Height - resizeStart.Height) > 0.5 || Math.Abs(Left - resizeStart.Left) > 0.5 || Math.Abs(Top - resizeStart.Top) > 0.5;
            if (expanded) { Group.ExpandedWidth = Width; Group.ExpandedHeight = Height; }
            else { Group.CompactWidth = Width; Group.CompactHeight = Height; }
            QueueSizeRefresh(); e.Handled = true;
        }
        private void FinishCardResize(object sender, DragCompletedEventArgs e)
        {
            if (!resizingCard || activeResizeThumb != sender) return;
            bool restore = e.Canceled || !resizeChanged;
            if (restore) {
                if (expanded) { Group.ExpandedWidth = resizeStoredWidth; Group.ExpandedHeight = resizeStoredHeight; }
                else { Group.CompactWidth = resizeStoredWidth; Group.CompactHeight = resizeStoredHeight; }
                applyingSize = true;
                try { MinHeight = MinimumModeHeight(resizeStart.Width); Width = resizeStart.Width; Height = resizeStart.Height; Left = resizeStart.Left; Top = resizeStart.Top; }
                finally { applyingSize = false; }
            }
            resizingCard = false; activeResizeThumb = null;
            EnsureVisible();
            if (!expanded) { anchorX = Left; anchorY = Top; }
            if (!restore) {
                if (expanded) { Group.ExpandedWidth = Width; Group.ExpandedHeight = Height; }
                else { Group.CompactWidth = Width; Group.CompactHeight = Height; Group.Left = Left; Group.Top = Top; }
                controller.Save();
            }
            QueueSizeRefresh();
        }
        private void ResetCardSize()
        {
            Group.CompactWidth = Group.CompactHeight = Group.ExpandedWidth = Group.ExpandedHeight = 0;
            ApplyModeSize(); controller.Save();
        }
        private void BuildCompact()
        {
            if (compactPreview != null) content.Children.Remove(compactPreview);
            if (compactAllButton != null) content.Children.Remove(compactAllButton);
            compactAllButton = null;
            var layout = CardGeometry.Preview(Width, Height, Group.Items.Count);
            var preview = new WrapPanel { ItemWidth = 56, ItemHeight = 56, Width = layout.Columns * 56, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            compactPreview = preview;
            foreach (var item in Group.Items.Take(layout.Capacity)) {
                var current = item;
                var tile = new Grid { Width = 52, Height = 56 };
                tile.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); tile.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
                tile.Children.Add(new Image { Source = ShellIcons.Get(item.Path, 48), Width = 26, Height = 26, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                var label = Ui.Text(Ui.DisplayName(item.Path), 10, cardText); label.Height = 26; label.TextAlignment = TextAlignment.Center; label.TextWrapping = TextWrapping.Wrap; label.TextTrimming = TextTrimming.CharacterEllipsis; label.LineHeight = 13; label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; label.VerticalAlignment = VerticalAlignment.Top; Grid.SetRow(label, 1); tile.Children.Add(label);
                DateTime lastOpened = DateTime.MinValue;
                var button = Ui.Button("", delegate {
                    DateTime now = DateTime.UtcNow;
                    if ((now - lastOpened).TotalMilliseconds < Forms.SystemInformation.DoubleClickTime) return;
                    lastOpened = now; OpenItem(current);
                }, false);
                button.Content = tile; button.Padding = new Thickness(0); button.Margin = new Thickness(2, 0, 2, 0); button.Background = Brushes.Transparent; button.Template = ItemButtonTemplate(darkCard);
                button.ToolTip = Ui.DisplayName(item.Path) + "\n" + item.Path + "\n单击直接打开"; button.ContextMenu = ItemMenu(item);
                System.Windows.Automation.AutomationProperties.SetName(button, Ui.DisplayName(item.Path));
                preview.Children.Add(button);
            }
            if (Group.Items.Count == 0) {
                var empty = Ui.Button("+", ShowAddMenu, false); empty.FontSize = 24; empty.Padding = new Thickness(0); empty.Background = Brushes.Transparent; empty.Foreground = cardMuted; empty.ToolTip = "拖入桌面快捷方式会移入分组；按住 Ctrl 拖入仅添加入口";
                System.Windows.Automation.AutomationProperties.SetName(empty, "添加项目"); preview.Children.Add(empty);
            }
            Grid.SetRow(preview, 2); content.Children.Add(preview);
            if (layout.HasMore) {
                var all = Ui.Button("", Expand, false); all.Width = 28; all.Height = 20; all.Padding = new Thickness(0); all.Margin = new Thickness(0, 4, 0, 0); all.HorizontalAlignment = HorizontalAlignment.Center; all.Background = Ui.Brush("#90FFFFFF"); all.Template = ItemButtonTemplate(darkCard);
                all.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1,1 L 5,1 L 5,5 L 1,5 Z M 8,1 L 12,1 L 12,5 L 8,5 Z M 1,8 L 5,8 L 5,12 L 1,12 Z M 8,8 L 12,8 L 12,12 L 8,12 Z"), Fill = Ui.Accent, Width = 11, Height = 11, Stretch = Stretch.Uniform };
                all.ToolTip = "查看全部 · " + Group.Items.Count + " 个项目";
                System.Windows.Automation.AutomationProperties.SetName(all, "查看全部 " + Group.Items.Count + " 个项目"); Grid.SetRow(all, 3); content.Children.Add(all);
                compactAllButton = all;
            }
        }
        private void BuildExpanded()
        {
            searchShell = new Border { CornerRadius = new CornerRadius(8), Background = Ui.Brush(darkCard ? "#18000000" : "#65FFFFFF"), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
            var searchGrid = new Grid(); searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); searchGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Ui.Text("⌕", 19, cardMuted); icon.Margin = new Thickness(0, 0, 9, 0); searchGrid.Children.Add(icon);
            search = new TextBox { FontSize = 12, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = cardInk, CaretBrush = cardInk, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "搜索此分组 (Ctrl+F)" };
            System.Windows.Automation.AutomationProperties.SetName(search, "搜索此分组");
            Grid.SetColumn(search, 1); searchGrid.Children.Add(search); searchShell.Child = searchGrid;
            Grid.SetRow(searchShell, 1); content.Children.Add(searchShell);
            itemsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            itemsScroll = new ScrollViewer { Content = itemsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
            Grid.SetRow(itemsScroll, 2); content.Children.Add(itemsScroll);
            resultLabel = Ui.Text("", 10, cardMuted);
            search.TextChanged += delegate { if (!rebuilding) FillItems(); }; FillItems();
        }
        private void FillItems()
        {
            if (itemsPanel == null) return; itemsPanel.Children.Clear();
            string query = search == null ? "" : search.Text.Trim();
            var matches = Group.Items.Where(i => query.Length == 0 || Ui.DisplayName(i.Path).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            resultLabel.Text = query.Length > 0 ? matches.Count + " / " + Group.Items.Count + " 个项目" : Group.Items.Count + " 个项目 · 双击打开";
            if (matches.Count == 0) {
                var empty = new StackPanel { Width = Math.Max(180, ExpandedColumns() * 80), Margin = new Thickness(0, 30, 0, 15), HorizontalAlignment = HorizontalAlignment.Center };
                var headline = Ui.Text(query.Length == 0 ? "拖入文件或文件夹" : "没有找到相关项目", 13, cardInk); headline.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(headline);
                itemsPanel.Children.Add(empty); FitExpandedHeight(); return;
            }
            foreach (var item in matches) {
                var current = item;
                var tile = new Grid { Width = 66, Height = 72 };
                tile.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); tile.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                tile.Children.Add(new Image { Source = ShellIcons.Get(item.Path, 48), Width = 34, Height = 34, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                var label = Ui.Text(Ui.DisplayName(item.Path), 11, cardText); label.Height = 30; label.TextAlignment = TextAlignment.Center; label.TextWrapping = TextWrapping.Wrap; label.TextTrimming = TextTrimming.CharacterEllipsis; label.LineHeight = 15; label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; label.VerticalAlignment = VerticalAlignment.Top; Grid.SetRow(label, 1); tile.Children.Add(label);
                var button = Ui.Button("", delegate { }, false); button.Width = 80; button.Height = 86; button.Padding = new Thickness(7); button.Background = Brushes.Transparent; button.Content = tile; button.Template = ItemButtonTemplate(darkCard);
                button.ToolTip = item.Path + (string.IsNullOrEmpty(item.OriginalPath) ? "" : "\n原位置：" + item.OriginalPath) + "\n按住拖动可调整顺序"; button.ContextMenu = ItemMenu(item); button.Tag = item;
                System.Windows.Automation.AutomationProperties.SetName(button, Ui.DisplayName(item.Path));
                bool itemPressed = false; Point itemDown = new Point();
                button.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { itemPressed = e.ClickCount == 1; itemDown = e.GetPosition(button); };
                button.PreviewMouseLeftButtonUp += delegate { itemPressed = false; };
                button.PreviewMouseMove += delegate(object s, MouseEventArgs e) {
                    if (!itemPressed || e.LeftButton != MouseButtonState.Pressed || draggingItem) return;
                    Point position = e.GetPosition(button);
                    if (Math.Abs(position.X - itemDown.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - itemDown.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    itemPressed = false; e.Handled = true; BeginItemDrag(button, current);
                };
                button.MouseDoubleClick += delegate(object s, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) { OpenItem(current); e.Handled = true; } };
                button.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { OpenItem(current); e.Handled = true; } };
                itemsPanel.Children.Add(button);
            }
            FitExpandedHeight();
        }
        private void OpenItem(ItemEntry item)
        {
            try { ShellIcons.Open(item.Path); Collapse(); }
            catch (Exception ex) { controller.Store.Log(ex); MessageBox.Show(this, "无法打开这个项目。它可能已被移动或删除。\n\n" + item.Path + "\n\n" + ex.Message + "\n\n可以右键选择“重新定位”。", "无法打开", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        private ContextMenu ItemMenu(ItemEntry item)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Ui.Menu("打开", delegate { OpenItem(item); }));
            menu.Items.Add(Ui.Menu("在资源管理器中显示", delegate { try { ShellIcons.Reveal(item.Path); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }));
            menu.Items.Add(Ui.Menu("复制完整路径", delegate { Clipboard.SetText(item.Path); }));
            if (FileActions.IsDesktopShortcut(item.Path)) menu.Items.Add(Ui.Menu("收纳这个桌面快捷方式", delegate { WithModal(delegate { CollectPaths(new string[] { item.Path }); }); }));
            if (!string.IsNullOrEmpty(item.OriginalPath)) menu.Items.Add(Ui.Menu("还原到原位置", delegate { WithModal(delegate { string error = FileActions.Restore(item); controller.Save(); RefreshItems(); if (!string.IsNullOrEmpty(error)) MessageBox.Show(this, error, "还原未完成"); }); }));
            var locate = new MenuItem { Header = "重新定位" };
            locate.Items.Add(Ui.Menu("选择文件 / 快捷方式…", delegate { Relocate(item, false); }));
            locate.Items.Add(Ui.Menu("选择文件夹…", delegate { Relocate(item, true); })); menu.Items.Add(locate);
            var move = new MenuItem { Header = "移动到分组" };
            foreach (var group in controller.Data.Groups.Where(g => g != Group)) { var dest = group; move.Items.Add(Ui.Menu(dest.Name, delegate { if (dest.Items.Any(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase))) return; Group.Items.Remove(item); dest.Items.Add(item); controller.Save(); RefreshItems(); foreach (var w in controller.Windows.Where(w => w.Group == dest)) w.RefreshItems(); })); }
            move.IsEnabled = move.Items.Count > 0; menu.Items.Add(move);
            menu.Items.Add(Ui.Menu("向前移动", delegate { int at = Group.Items.IndexOf(item); if (at > 0) { Group.Items.RemoveAt(at); Group.Items.Insert(at - 1, item); controller.Save(); RefreshItems(); } }));
            menu.Items.Add(Ui.Menu("向后移动", delegate { int at = Group.Items.IndexOf(item); if (at < Group.Items.Count - 1) { Group.Items.RemoveAt(at); Group.Items.Insert(at + 1, item); controller.Save(); RefreshItems(); } }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.Menu(string.IsNullOrEmpty(item.OriginalPath) ? "移出分组（保留原文件）" : "还原原件并移出分组", delegate { WithModal(delegate { if (!string.IsNullOrEmpty(item.OriginalPath)) { string error = FileActions.Restore(item); controller.Save(); if (!string.IsNullOrEmpty(error)) { RefreshItems(); MessageBox.Show(this, error, "还原未完成"); return; } } Group.Items.Remove(item); controller.Save(); RefreshItems(); }); }));
            if (!string.IsNullOrEmpty(item.OriginalPath)) menu.Items.Add(Ui.Menu("仅移出入口，保留当前位置…", delegate { DetachCollected(item); }));
            return menu;
        }
        private ContextMenu GroupMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(Ui.Menu("整理桌面（移入分组）…", ImportDesktop));
            int desktopShortcutCount = Group.Items.Count(i => FileActions.IsDesktopShortcut(i.Path));
            if (desktopShortcutCount > 0) menu.Items.Add(Ui.Menu("收纳本组桌面快捷方式（" + desktopShortcutCount + " 项）", CollectGroupDesktopShortcuts));
            menu.Items.Add(Ui.Menu("添加文件 / 快捷方式…", AddFiles)); menu.Items.Add(Ui.Menu("添加文件夹入口…", AddFolder));
            menu.Items.Add(Ui.Menu("仅添加文件 / 快捷方式入口…", delegate { PickFiles(true); }));
            menu.Items.Add(Ui.Menu("搜索此分组 (Ctrl+F)", ShowSearch));
            menu.Items.Add(Ui.Menu("恢复默认大小", ResetCardSize));
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.Menu("新建分组…", delegate { WithModal(delegate { controller.NewGroup(this); }); }));
            menu.Items.Add(Ui.Menu("重命名分组…", Rename));
            var colors = new MenuItem { Header = "分组颜色" }; string[] names = { "鼠尾草", "雾蓝", "奶杏", "淡紫", "柔粉" };
            for (int i = 0; i < names.Length; i++) { string color = PresetColors[i]; var m = Ui.Menu(names[i], delegate { SetGroupColor(color); }); m.IsChecked = string.Equals(Tint(), color, StringComparison.OrdinalIgnoreCase); m.Icon = new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(4), Background = Ui.Brush(color), BorderBrush = Ui.Brush("#BEC9C0"), BorderThickness = new Thickness(1) }; colors.Items.Add(m); }
            colors.Items.Add(new Separator());
            var customColor = Ui.Menu("自定义颜色…", ChooseGroupColor); customColor.ToolTip = "打开调色盘，自由选择颜色；当前 " + Tint(); customColor.Icon = new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(4), Background = Ui.Brush(Tint()), BorderBrush = Ui.Brush("#BEC9C0"), BorderThickness = new Thickness(1) }; colors.Items.Add(customColor); menu.Items.Add(colors);
            menu.Items.Add(Ui.Menu("按名称排序", delegate { Group.Items = Group.Items.OrderBy(i => Ui.DisplayName(i.Path), StringComparer.CurrentCultureIgnoreCase).ToList(); controller.Save(); RefreshItems(); }));
            var blur = new MenuItem { Header = "点击其他窗口自动收起", IsCheckable = true, IsChecked = controller.Data.CloseOnBlur }; blur.Click += delegate { controller.Data.CloseOnBlur = blur.IsChecked; controller.Save(); }; menu.Items.Add(blur);
            var top = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = controller.Data.AlwaysOnTop }; top.Click += delegate { controller.SetTopmost(top.IsChecked); }; menu.Items.Add(top);
            var startup = new MenuItem { Header = "开机启动", IsCheckable = true, IsChecked = controller.IsStartupEnabled() }; startup.Click += delegate { controller.SetStartup(startup.IsChecked); }; menu.Items.Add(startup);
            menu.Items.Add(new Separator()); menu.Items.Add(Ui.Menu("使用说明", controller.Help));
            menu.Items.Add(Ui.Menu("隐藏此分组（托盘可恢复）", delegate { Collapse(); Hide(); }));
            menu.Items.Add(Ui.Menu("删除此分组…", delegate { WithModal(delegate { controller.DeleteGroup(this); }); }));
            menu.Items.Add(Ui.Menu("退出程序", controller.Exit)); return menu;
        }
        private void ShowAddMenu() { var menu = new ContextMenu(); menu.Items.Add(Ui.Menu("整理桌面（移入分组）…", ImportDesktop)); menu.Items.Add(Ui.Menu("文件 / 快捷方式…", AddFiles)); menu.Items.Add(Ui.Menu("文件夹入口…", AddFolder)); menu.Items.Add(Ui.Menu("仅添加入口（不移动）…", delegate { PickFiles(true); })); menu.PlacementTarget = card; menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true; }
        private void WithModal(Action action) { modal = true; try { action(); } finally { modal = false; } }
        private sealed class ColorDialogOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public ColorDialogOwner(Window window) { Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
        }
        private void SetGroupColor(string color) { Group.Color = color; controller.Save(); Build(); }
        private void ChooseGroupColor() { WithModal(delegate {
            Color current = Ui.Brush(Tint()).Color;
            using (var picker = new Forms.ColorDialog { FullOpen = true, AllowFullOpen = true, AnyColor = true, SolidColorOnly = true, Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B) }) {
                if (picker.ShowDialog(new ColorDialogOwner(this)) == Forms.DialogResult.OK)
                    SetGroupColor("#" + picker.Color.R.ToString("X2") + picker.Color.G.ToString("X2") + picker.Color.B.ToString("X2"));
            }
        }); }
        private void AddFiles() { PickFiles(false); }
        private void PickFiles(bool referencesOnly) { WithModal(delegate { var picker = new OpenFileDialog { Title = referencesOnly ? "仅添加入口（原位置不变）" : "桌面快捷方式会移入分组，其他文件只添加入口", Multiselect = true, CheckFileExists = true, DereferenceLinks = false, Filter = "所有文件|*.*" }; if (picker.ShowDialog(this) == true) AddPaths(picker.FileNames, referencesOnly); }); }
        private void AddFolder() { WithModal(delegate { using (var picker = new Forms.FolderBrowserDialog { Description = "选择文件夹（仅添加入口，不移动原件）", ShowNewFolderButton = true }) if (picker.ShowDialog() == Forms.DialogResult.OK) AddPaths(new[] { picker.SelectedPath }); }); }
        private void AddPaths(IEnumerable<string> paths, bool referencesOnly = false) { var errors = FileActions.AddToGroup(Group, paths, controller.Store.StorageDirectory, referencesOnly); controller.Save(); RefreshItems(); controller.ReportErrors(this, errors); }
        private void CollectPaths(IEnumerable<string> paths) { var errors = FileActions.CollectToStorage(Group, paths, controller.Store.StorageDirectory); controller.Save(); RefreshItems(); controller.ReportErrors(this, errors); }
        private void CollectGroupDesktopShortcuts() { WithModal(delegate { CollectPaths(Group.Items.Where(i => FileActions.IsDesktopShortcut(i.Path)).Select(i => i.Path).ToArray()); }); }
        private void Relocate(ItemEntry item, bool folder) { WithModal(delegate {
            string path = null;
            if (folder) { using (var picker = new Forms.FolderBrowserDialog { Description = "重新选择这个项目的当前位置", ShowNewFolderButton = false }) if (picker.ShowDialog() == Forms.DialogResult.OK) path = picker.SelectedPath; }
            else { var picker = new OpenFileDialog { Title = "重新选择这个项目的当前位置", DereferenceLinks = false, Filter = "所有文件|*.*" }; if (picker.ShowDialog(this) == true) path = picker.FileName; }
            if (path == null) return;
            if (Group.Items.Any(i => i != item && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "该项目已在分组中。", "重新定位"); return; }
            item.Path = path; controller.Save(); RefreshItems();
        }); }
        private void DetachCollected(ItemEntry item) { WithModal(delegate {
            if (MessageBox.Show(this, "只移除入口，原件保留在当前位置。\n\n当前位置：" + item.Path + "\n原位置：" + item.OriginalPath + "\n\n两处路径会保存在 data\\移出记录.txt，供以后手动还原。", "保留原件并移出入口", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            try { File.AppendAllText(Path.Combine(controller.Store.DataDirectory, "移出记录.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n分组：" + Group.Name + "\r\n当前位置：" + item.Path + "\r\n原位置：" + item.OriginalPath + "\r\n\r\n", System.Text.Encoding.UTF8); }
            catch (Exception ex) { MessageBox.Show(this, "无法保存还原记录：" + ex.Message); return; }
            Group.Items.Remove(item); controller.Save(); RefreshItems();
        }); }
        private void Rename() { WithModal(delegate { string name = Ui.AskText(this, "重命名分组", "分组名称", Group.Name); if (name != null) { Group.Name = name; Title = "大文件夹 · " + name; controller.Save(); Build(); } }); }
        private void ImportDesktop() { WithModal(delegate { new DesktopDialog(this, controller, Group).ShowDialog(); RefreshItems(); }); }
        private void BeginItemDrag(Button source, ItemEntry item)
        {
            var data = new DataObject(); data.SetData(ItemDragFormat, new ItemDragData { GroupId = Group.Id, ItemId = item.Id }, false);
            draggingItem = true; source.Opacity = 0.45; source.ReleaseMouseCapture();
            try { DragDrop.DoDragDrop(source, data, DragDropEffects.Move); }
            finally { draggingItem = false; source.Opacity = 1; ClearDropIndicator(); }
        }
        private ItemDragData GetItemDrag(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(ItemDragFormat, false)) return null;
            var data = e.Data.GetData(ItemDragFormat, false) as ItemDragData;
            return data != null && expanded && data.GroupId == Group.Id && Group.Items.Any(i => i.Id == data.ItemId) ? data : null;
        }
        private DropPosition FindDropPosition(Point point)
        {
            if (!expanded || itemsPanel == null) return null;
            var buttons = itemsPanel.Children.OfType<Button>().Where(b => b.Tag is ItemEntry).ToList();
            if (buttons.Count == 0) return null;
            var firstButton = buttons[0]; var lastButton = buttons[buttons.Count - 1];
            if (point.Y < firstButton.TranslatePoint(new Point(), itemsPanel).Y) return new DropPosition { Target = firstButton, After = false };
            if (point.Y >= lastButton.TranslatePoint(new Point(), itemsPanel).Y + lastButton.ActualHeight) return new DropPosition { Target = lastButton, After = true };
            // Pick the visible row first, then the insertion side of its nearest icon.
            double rowTop = buttons[buttons.Count - 1].TranslatePoint(new Point(), itemsPanel).Y;
            foreach (var button in buttons) {
                Point top = button.TranslatePoint(new Point(), itemsPanel);
                if (point.Y < top.Y + button.ActualHeight) { rowTop = top.Y; break; }
            }
            var row = buttons.Where(b => Math.Abs(b.TranslatePoint(new Point(), itemsPanel).Y - rowTop) < 1).ToList();
            var target = row[row.Count - 1];
            foreach (var button in row) {
                if (point.X < button.TranslatePoint(new Point(), itemsPanel).X + button.ActualWidth) { target = button; break; }
            }
            Point location = target.TranslatePoint(new Point(), itemsPanel);
            return new DropPosition { Target = target, After = point.X >= location.X + target.ActualWidth / 2 };
        }
        private void ShowDropIndicator(DropPosition position)
        {
            if (position == null || dropMarker == null) { ClearDropIndicator(); return; }
            Point location = position.Target.TranslatePoint(new Point(), dragOverlay);
            Canvas.SetLeft(dropMarker, location.X + (position.After ? position.Target.ActualWidth - 2 : 0));
            Canvas.SetTop(dropMarker, location.Y + 6);
            dropMarker.Height = Math.Max(10, position.Target.ActualHeight - 12);
            dropMarker.Visibility = Visibility.Visible;
        }
        private void UpdateDragScroll(DragEventArgs e)
        {
            dragViewportPoint = e.GetPosition(itemsScroll);
            dragScrollDirection = dragViewportPoint.Y >= 0 && dragViewportPoint.Y < 22 ? -1 : dragViewportPoint.Y > itemsScroll.ActualHeight - 22 && dragViewportPoint.Y <= itemsScroll.ActualHeight ? 1 : 0;
            if (dragScrollDirection == 0 || itemsScroll.ScrollableHeight <= 0) { if (dragScrollTimer != null) dragScrollTimer.Stop(); return; }
            if (dragScrollTimer == null) {
                dragScrollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(65) };
                dragScrollTimer.Tick += delegate {
                    if (!draggingItem || !expanded || dragScrollDirection == 0) { dragScrollTimer.Stop(); return; }
                    itemsScroll.ScrollToVerticalOffset(itemsScroll.VerticalOffset + dragScrollDirection * 18);
                    itemsScroll.UpdateLayout();
                    ShowDropIndicator(FindDropPosition(itemsScroll.TranslatePoint(dragViewportPoint, itemsPanel)));
                };
            }
            dragScrollTimer.Start();
        }
        private void ClearDropIndicator() { if (dropMarker != null) dropMarker.Visibility = Visibility.Collapsed; if (dragScrollTimer != null) dragScrollTimer.Stop(); dragScrollDirection = 0; }
        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(ItemDragFormat, false)) {
                var data = GetItemDrag(e);
                var position = data == null ? null : FindDropPosition(e.GetPosition(itemsPanel));
                e.Effects = position == null ? DragDropEffects.None : DragDropEffects.Move;
                ShowDropIndicator(position); if (position != null) UpdateDragScroll(e); e.Handled = true; return;
            }
            ClearDropIndicator();
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool referenceOnly = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                e.Effects = !referenceOnly && paths != null && paths.Any(FileActions.IsDesktopShortcut) ? DragDropEffects.Move : DragDropEffects.Link;
                if ((e.Effects & e.AllowedEffects) == 0) e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            } else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(ItemDragFormat, false)) {
                var data = GetItemDrag(e);
                var position = data == null ? null : FindDropPosition(e.GetPosition(itemsPanel));
                ClearDropIndicator();
                if (position != null && ItemOrdering.MoveRelative(Group, data.ItemId, ((ItemEntry)position.Target.Tag).Id, position.After)) { controller.Save(); FillItems(); }
                e.Effects = position == null ? DragDropEffects.None : DragDropEffects.Move; e.Handled = true; return;
            }
            ClearDropIndicator();
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                bool referenceOnly = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                WithModal(delegate { AddPaths(paths, referenceOnly); });
                // Collection is handled by this application; the drag source must not remove files itself.
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
        private void OnKey(object sender, KeyEventArgs e)
        {
            if (resizingCard) { if (e.Key == Key.Escape && activeResizeThumb != null) activeResizeThumb.CancelDrag(); e.Handled = true; return; }
            if (canMoveCard) { if (e.Key == Key.Escape) FinishCardMove(true); e.Handled = true; return; }
            if (e.Key == Key.Escape) { Collapse(); e.Handled = true; }
            if (e.Key == Key.F2) { Rename(); e.Handled = true; }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                if (e.Key == Key.F) { ShowSearch(); e.Handled = true; }
                if (e.Key == Key.O) { AddFiles(); e.Handled = true; }
                if (e.Key == Key.N) { WithModal(delegate { controller.NewGroup(this); }); e.Handled = true; }
            }
        }
    }
}
