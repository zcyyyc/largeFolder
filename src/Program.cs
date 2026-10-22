using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LargeFolder
{
    public static class Program
    {
        public static bool InspectMode;
        [STAThread]
        public static void Main(string[] args)
        {
            InspectMode = args.Contains("--inspect");
            if (args.Contains("--exit")) { try { EventWaitHandle.OpenExisting("Local\\LargeFolder.Exit.v1").Set(); } catch { } return; }
            bool first;
            using (var mutex = new Mutex(true, "Local\\LargeFolder.App.v1", out first))
            {
                if (!first) { try { EventWaitHandle.OpenExisting("Local\\LargeFolder.Show.v1").Set(); } catch { } return; }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var root = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--data-dir") root = Path.GetFullPath(args[i + 1]);
                var controller = new AppController(app, root);
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
                    controller.Store.Log(e.Exception); e.Handled = true;
                    MessageBox.Show("操作未完成：" + e.Exception.Message + "\n文件不会因此被删除。", "大文件夹", MessageBoxButton.OK, MessageBoxImage.Warning);
                };
                app.Startup += delegate { controller.Start(); };
                app.Exit += delegate { controller.Cleanup(); };
                app.Run();
            }
        }
    }

    public sealed class AppController
    {
        public readonly DataStore Store;
        public readonly AppData Data;
        public readonly List<FolderWindow> Windows = new List<FolderWindow>();
        private readonly Application app;
        private Forms.NotifyIcon tray;
        private EventWaitHandle showEvent, exitEvent;
        private RegisteredWaitHandle showWait, exitWait;
        private bool exiting;
        public bool IsExiting { get { return exiting; } }
        public AppController(Application app, string root) { this.app = app; Store = new DataStore(root); Data = Store.Load(); }
        public void Start()
        {
            tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location), Text = "大文件夹 · 双击显示所有分组", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示所有分组", null, delegate { ShowAll(); });
            menu.Items.Add("收起所有分组", null, delegate { foreach (var w in Windows) w.Collapse(); });
            menu.Items.Add("新建分组…", null, delegate { NewGroup(null); });
            menu.Items.Add("打开程序目录", null, delegate { ShellIcons.Open(Store.RootDirectory); });
            menu.Items.Add("使用说明", null, delegate { Help(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            var top = new Forms.ToolStripMenuItem("始终置顶") { CheckOnClick = true, Checked = Data.AlwaysOnTop };
            top.CheckedChanged += delegate { SetTopmost(top.Checked); };
            menu.Items.Add(top);
            var startup = new Forms.ToolStripMenuItem("开机启动") { CheckOnClick = true, Checked = IsStartupEnabled() };
            startup.CheckedChanged += delegate { SetStartup(startup.Checked); };
            menu.Items.Add(startup);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出大文件夹", null, delegate { Exit(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowAll(); };
            foreach (var group in Data.Groups) OpenGroup(group);
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LargeFolder.Show.v1");
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LargeFolder.Exit.v1");
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { app.Dispatcher.BeginInvoke(new Action(ShowAll)); }, null, -1, false);
            exitWait = ThreadPool.RegisterWaitForSingleObject(exitEvent, delegate { app.Dispatcher.BeginInvoke(new Action(Exit)); }, null, -1, true);
        }
        private void OpenGroup(FolderGroup group) { var w = new FolderWindow(this, group); Windows.Add(w); w.Show(); }
        public void ShowAll() { foreach (var w in Windows) { w.Show(); w.WindowState = WindowState.Normal; w.EnsureVisible(); w.Activate(); } }
        public void Save()
        {
            try { Store.Save(Data); }
            catch (Exception ex) { Store.Log(ex); MessageBox.Show("设置暂时无法保存：" + ex.Message + "\n请检查程序目录是否可写。", "大文件夹"); }
        }
        public void NewGroup(Window owner)
        {
            string name = Ui.AskText(owner, "新建分组", "给这一组起个名字", "新分组");
            if (name == null) return;
            var group = DataStore.NewGroup(name, Data.Groups.Count);
            Data.Groups.Add(group); Save(); OpenGroup(group);
        }
        public void DeleteGroup(FolderWindow window)
        {
            if (window.Group.Items.Any(i => !string.IsNullOrEmpty(i.OriginalPath))) {
                MessageBox.Show(window, "这个分组里还有收纳的原件。请先逐项右键选择“还原到原位置”，再删除分组。", "保留还原记录"); return;
            }
            if (MessageBox.Show(window, "删除“" + window.Group.Name + "”分组及其中的入口？\n原文件和文件夹均会保留。", "删除分组", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Windows.Remove(window); Data.Groups.Remove(window.Group); window.ClosePermanently();
            if (Data.Groups.Count == 0) { var group = DataStore.NewGroup("常用", 0); Data.Groups.Add(group); OpenGroup(group); }
            Save();
        }
        public void SetTopmost(bool value) { Data.AlwaysOnTop = value; foreach (var w in Windows) w.Topmost = value; Save(); }
        public bool IsStartupEnabled() { using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) return key != null && key.GetValue("LargeFolder") != null; }
        public void SetStartup(bool value)
        {
            try {
                using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) {
                    if (value) key.SetValue("LargeFolder", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                    else key.DeleteValue("LargeFolder", false);
                }
                Data.StartWithWindows = value; Save();
            } catch (Exception ex) { Store.Log(ex); MessageBox.Show("无法修改开机启动：" + ex.Message, "大文件夹"); }
        }
        public void Help() { string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "使用说明.txt"); if (File.Exists(file)) ShellIcons.Open(file); }
        public void Exit() { if (exiting) return; exiting = true; Save(); app.Shutdown(); }
        public void Cleanup() { if (showWait != null) showWait.Unregister(null); if (exitWait != null) exitWait.Unregister(null); if (tray != null) { tray.Visible = false; tray.Dispose(); } if (showEvent != null) showEvent.Dispose(); if (exitEvent != null) exitEvent.Dispose(); }
        public void ReportErrors(Window owner, List<string> errors) { if (errors.Count > 0) MessageBox.Show(owner, string.Join("\n\n", errors.Take(8).ToArray()) + (errors.Count > 8 ? "\n另有 " + (errors.Count - 8) + " 项未完成。" : ""), "部分项目未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public static class Ui
    {
        public static readonly Brush Ink = Brush("#23382F"), Muted = Brush("#74847B"), Accent = Brush("#337557");
        public static SolidColorBrush Brush(string color) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(color); b.Freeze(); return b; }
        public static TextBlock Text(string text, double size, Brush color) { return new TextBlock { Text = text, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center }; }
        public static Button Button(string text, Action action, bool primary)
        {
            var b = new Button { Content = text, Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(0), FontSize = 12, Cursor = System.Windows.Input.Cursors.Hand, Background = primary ? Accent : Brush("#F1F4F1"), Foreground = primary ? Brushes.White : Ink, BorderThickness = new Thickness(0), FocusVisualStyle = null };
            b.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border x:Name='bd' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='9' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.78'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.58'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            b.Click += delegate { action(); }; return b;
        }
        public static MenuItem Menu(string text, Action action) { var m = new MenuItem { Header = text, Padding = new Thickness(7, 5, 7, 5) }; m.Click += delegate { action(); }; return m; }
        public static string DisplayName(string path) { var name = Path.GetFileName(path.TrimEnd('\\', '/')); if (string.IsNullOrEmpty(name)) return path; if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(name); return name; }
        public static Window Dialog(Window owner, string title, double width, double height)
        {
            return new Window { Title = title, Owner = owner, Width = width, Height = height, WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner, Background = Brush("#FBFCF9"), FontFamily = new FontFamily("Microsoft YaHei UI"), Foreground = Ink, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = Program.InspectMode };
        }
        public static string AskText(Window owner, string title, string label, string initial)
        {
            var w = Dialog(owner, title, 378, 208); var stack = new StackPanel { Margin = new Thickness(24) }; w.Content = stack;
            stack.Children.Add(Text(label, 14, Ink));
            var input = new TextBox { Text = initial, FontSize = 16, MaxLength = 32, Padding = new Thickness(8), Margin = new Thickness(0, 12, 0, 18) }; stack.Children.Add(input);
            string result = null; var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = Button("取消", delegate { w.Close(); }, false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(cancel);
            var ok = Button("确定", delegate { if (input.Text.Trim().Length == 0) { input.Focus(); return; } result = input.Text.Trim(); w.DialogResult = true; }, true); ok.IsDefault = true; row.Children.Add(ok); stack.Children.Add(row);
            w.Loaded += delegate { input.Focus(); input.SelectAll(); }; w.ShowDialog(); return result;
        }
    }
}
