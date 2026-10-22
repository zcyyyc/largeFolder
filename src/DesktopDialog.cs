using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LargeFolder
{
    public sealed class DesktopDialog : Window
    {
        private readonly List<CheckBox> checks = new List<CheckBox>();
        private readonly AppController controller;
        private readonly FolderGroup group;
        private Button apply;
        private TextBlock status;
        private CheckBox collect;
        public DesktopDialog(Window owner, AppController controller, FolderGroup group)
        {
            this.controller = controller; this.group = group;
            Title = "整理桌面 · " + group.Name; Owner = owner; Width = 600; Height = 646; MinWidth = 500; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Brush("#FBFCF9"); FontFamily = new FontFamily("Microsoft YaHei UI"); Foreground = Ui.Ink; ShowInTaskbar = Program.InspectMode;
            var root = new Grid { Margin = new Thickness(25) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new StackPanel(); var title = Ui.Text("把桌面留给壁纸", 24, Ui.Ink); title.FontWeight = FontWeights.SemiBold; head.Children.Add(title);
            var desc = Ui.Text("勾选要放进“" + group.Name + "”的项目。\n默认只添加入口；勾选下方“移动原件”才会减少桌面图标。", 12, Ui.Muted); desc.Margin = new Thickness(0, 10, 0, 14); desc.LineHeight = 22; head.Children.Add(desc);
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var all = Ui.Button("全选", delegate { foreach (var c in checks) c.IsChecked = true; }, false); commands.Children.Add(all);
            var clear = Ui.Button("清空选择", delegate { foreach (var c in checks) c.IsChecked = false; }, false); clear.Margin = new Thickness(8, 0, 0, 0); commands.Children.Add(clear); head.Children.Add(commands); root.Children.Add(head);
            var list = new StackPanel();
            foreach (string path in FileActions.GetDesktopItems()) {
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new Image { Source = ShellIcons.Get(path, 32), Width = 25, Height = 25, HorizontalAlignment = HorizontalAlignment.Left });
                var label = Ui.Text(Ui.DisplayName(path), 12, Ui.Ink); label.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(label, 1); row.Children.Add(label);
                var check = new CheckBox { Content = row, Tag = path, Padding = new Thickness(9, 7, 7, 7), Margin = new Thickness(0, 2, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = path };
                check.Checked += delegate { UpdateStatus(); }; check.Unchecked += delegate { UpdateStatus(); }; checks.Add(check); list.Children.Add(check);
            }
            if (checks.Count == 0) list.Children.Add(Ui.Text("桌面上没有可收纳的普通文件。\n系统图标（此电脑、回收站）不在此列表中。", 13, Ui.Muted));
            var listBorder = new Border { CornerRadius = new CornerRadius(12), Background = Brushes.White, BorderBrush = Ui.Brush("#E1E8DF"), BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            Grid.SetRow(listBorder, 1); root.Children.Add(listBorder);
            var foot = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            collect = new CheckBox { Content = "移动原件到收纳目录（桌面图标消失，可还原）", FontSize = 12, Foreground = Ui.Ink, Margin = new Thickness(0, 0, 0, 8) }; collect.Checked += delegate { UpdateStatus(); }; collect.Unchecked += delegate { UpdateStatus(); }; foot.Children.Add(collect);
            var pathHint = Ui.Text("收纳位置：" + controller.Store.StorageDirectory, 10, Ui.Muted); pathHint.TextTrimming = TextTrimming.CharacterEllipsis; foot.Children.Add(pathHint);
            var bottom = new Grid { Margin = new Thickness(0, 18, 0, 0) }; bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            status = Ui.Text("", 11, Ui.Muted); bottom.Children.Add(status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; var cancel = Ui.Button("取消", delegate { Close(); }, false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 9, 0); buttons.Children.Add(cancel);
            apply = Ui.Button("添加所选入口", Apply, true); buttons.Children.Add(apply); Grid.SetColumn(buttons, 1); bottom.Children.Add(buttons); foot.Children.Add(bottom);
            Grid.SetRow(foot, 2); root.Children.Add(foot); UpdateStatus();
        }
        private void UpdateStatus() { if (status == null || apply == null) return; int count = checks.Count(c => c.IsChecked == true); status.Text = "已选 " + count + " / " + checks.Count + " 项"; apply.IsEnabled = count > 0; apply.Content = collect != null && collect.IsChecked == true ? "收纳所选原件" : "添加所选入口"; }
        private void Apply()
        {
            var paths = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
            IsEnabled = false; Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try { var errors = collect.IsChecked == true ? FileActions.CollectToStorage(group, paths, controller.Store.StorageDirectory) : FileActions.AddReferences(group, paths); controller.Save(); controller.ReportErrors(this, errors); DialogResult = true; }
            finally { Mouse.OverrideCursor = null; IsEnabled = true; }
        }
    }
}
