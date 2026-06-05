using LamFiler.Models;
using LamFiler.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace LamFiler
{
    public partial class MainWindow : Window
    {
        public class FileItem
        {
            public string Name      { get; set; } = "";
            public string Icon      { get; set; } = "";
            public string IconColor { get; set; } = "#858585";
            public string Modified  { get; set; } = "";
            public string Size      { get; set; } = "";
            public string TypeExt   { get; set; } = "";
            public string FullPath  { get; set; } = "";
            public bool   IsDir     { get; set; }
            public double Opacity   { get; set; } = 1.0;
        }

        public class BreadcrumbPart
        {
            public string Label { get; set; } = "";
            public string Path  { get; set; } = "";
        }

        private string _currentPath = "";
        private readonly Stack<string> _backStack    = new();
        private readonly Stack<string> _forwardStack = new();
        private FileSystemWatcher? _watcher;
        private ObservableCollection<FileItem> _allItems = new();

        public MainWindow()
        {
            InitializeComponent();
            BuildSidebar();
            Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        // ════════════════════════════════════════════════════════════════════
        //  НАВИГАЦИЯ
        // ════════════════════════════════════════════════════════════════════

        private void Navigate(string path, bool addToBack = true)
        {
            if (!Directory.Exists(path)) return;
            if (addToBack && _currentPath != "")
            {
                _backStack.Push(_currentPath);
                _forwardStack.Clear();
            }
            _currentPath = path;
            LoadDirectory(path);
            UpdateBreadcrumb(path);
            UpdateNavButtons();
            StatusRight.Text = path;
        }

        private void LoadDirectory(string path, bool restoreSelection = false, int selectIndex = 0)
        {
            _allItems.Clear();
            try
            {
                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                {
                    try { Directory.GetFileSystemEntries(dir); }
                    catch { continue; }
                    var info = new DirectoryInfo(dir);
                    _allItems.Add(new FileItem
                    {
                        Name = info.Name, Icon = "📁", IconColor = "#FAC775",
                        Modified = info.LastWriteTime.ToString("dd MMM  HH:mm"),
                        Size = "—", TypeExt = "Folder", FullPath = dir, IsDir = true
                    });
                }
                foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
                {
                    var info = new FileInfo(file);
                    _allItems.Add(new FileItem
                    {
                        Name = info.Name, Icon = GetFileIcon(info.Extension),
                        IconColor = GetIconColor(info.Extension),
                        Modified = info.LastWriteTime.ToString("dd MMM  HH:mm"),
                        Size = FormatSize(info.Length),
                        TypeExt = info.Extension.ToLowerInvariant(),
                        FullPath = file, IsDir = false
                    });
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                MessageBox.Show("Нет доступа к этой папке.", "Lambada",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ApplyFilter(SearchBox.Text);
            SetupWatcher(path);
            AttachHoverTracking();

            if (restoreSelection && FileList.Items.Count > 0)
            {
                var idx = Math.Min(selectIndex, FileList.Items.Count - 1);
                FileList.SelectedIndex = idx;
                FileList.ScrollIntoView(FileList.Items[idx]);
                Dispatcher.InvokeAsync(() =>
                {
                    var lvi = FileList.ItemContainerGenerator.ContainerFromIndex(idx) as ListViewItem;
                    lvi?.Focus();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (!restoreSelection && FileList.Items.Count > 0)
            {
                FileList.SelectedIndex = 0;
                FileList.ScrollIntoView(FileList.Items[0]);
                Dispatcher.InvokeAsync(() =>
                {
                    var lvi = FileList.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
                    lvi?.Focus();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void ApplyFilter(string query)
        {
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allItems.ToList()
                : _allItems.Where(f =>
                    f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.TypeExt.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            FileList.ItemsSource = filtered;
            StatusLeft.Text = $"{filtered.Count} items";
        }

        // ════════════════════════════════════════════════════════════════════
        //  FileSystemWatcher
        // ════════════════════════════════════════════════════════════════════

        private void SetupWatcher(string path)
        {
            _watcher?.Dispose();
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFsChanged;
            _watcher.Deleted += OnFsChanged;
            _watcher.Renamed += OnFsChanged;
        }

        private bool _suppressWatcher = false;

        private void OnFsChanged(object sender, FileSystemEventArgs e)
            => Dispatcher.InvokeAsync(() => { if (!_suppressWatcher) LoadDirectory(_currentPath); });

        // ════════════════════════════════════════════════════════════════════
        //  BREADCRUMB
        // ════════════════════════════════════════════════════════════════════

        private void UpdateBreadcrumb(string path)
        {
            var parts = new List<BreadcrumbPart>();
            var current = path;
            while (true)
            {
                var parent = System.IO.Path.GetDirectoryName(current);
                parts.Insert(0, new BreadcrumbPart
                {
                    Label = parent == null ? current : System.IO.Path.GetFileName(current),
                    Path  = current
                });
                if (parent == null) break;
                current = parent;
            }
            Breadcrumb.ItemsSource = parts;
        }

        private void BreadcrumbPart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string p) Navigate(p);
        }

        // ════════════════════════════════════════════════════════════════════
        //  КНОПКИ НАЗАД / ВПЕРЁД
        // ════════════════════════════════════════════════════════════════════

        private void UpdateNavButtons()
        {
            BtnBack.IsEnabled    = _backStack.Count > 0;
            BtnForward.IsEnabled = _forwardStack.Count > 0;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_backStack.Count == 0) return;
            _forwardStack.Push(_currentPath);
            Navigate(_backStack.Pop(), addToBack: false);
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardStack.Count == 0) return;
            _backStack.Push(_currentPath);
            Navigate(_forwardStack.Pop(), addToBack: false);
        }

        // ════════════════════════════════════════════════════════════════════
        //  ДВОЙНОЙ КЛИК
        // ════════════════════════════════════════════════════════════════════

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    if (fi.IsDir) Navigate(fi.FullPath);
                    else FileContextMenuService.OpenDefault(fi.FullPath);
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  ПРЕВЬЮ (правая панель)
        // ════════════════════════════════════════════════════════════════════

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileList.SelectedItem is not FileItem item) return;
            var selected = FileList.SelectedItems.Count;
            StatusLeft.Text = selected > 1
                ? $"{selected} selected"
                : (FileList.ItemsSource is List<FileItem> list ? $"{list.Count} items" : "");
            ShowPreview(item);
        }

        private void ShowPreview(FileItem item)
        {
            PreviewMeta.Children.Clear();
            PreviewImage.Opacity = 0;
            PreviewIcon.Visibility = Visibility.Visible;
            PreviewIcon.Text = item.IsDir ? "📁" : GetFileIcon(System.IO.Path.GetExtension(item.FullPath));

            var imgExt = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            if (!item.IsDir && imgExt.Contains(item.TypeExt.ToLower()))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(item.FullPath);
                    bmp.DecodePixelWidth = 400;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    PreviewImage.Source = bmp;
                    PreviewImage.Opacity = 1;
                    PreviewIcon.Visibility = Visibility.Collapsed;
                }
                catch { }
            }

            AddMeta("Name", item.Name);
            if (!item.IsDir) { AddMeta("Size", item.Size); AddMeta("Type", item.TypeExt); }
            AddMeta("Modified", item.Modified);

            PreviewActions.Visibility = Visibility.Visible;
            BtnPreviewOpenWith.Visibility = item.IsDir ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AddMeta(string key, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            var k = new TextBlock { Text = key, FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)) };
            var v = new TextBlock { Text = value, FontSize = 12, TextAlignment = TextAlignment.Right,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(k, 0); Grid.SetColumn(v, 1);
            row.Children.Add(k); row.Children.Add(v);
            PreviewMeta.Children.Add(row);
            PreviewMeta.Children.Add(new Border
            {
                Height = 0.5,
                Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        private void BtnPreviewOpen_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not FileItem item) return;
            if (item.IsDir) Navigate(item.FullPath);
            else FileContextMenuService.OpenDefault(item.FullPath);
        }

        private void BtnPreviewOpenWith_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not FileItem item) return;
            var apps = FileContextMenuService.GetOpenWithApps(item.FullPath);
            if (apps.Count == 0) { FileContextMenuService.OpenDefault(item.FullPath); return; }
            var menu = BuildContextMenu();
            foreach (var app in apps)
            {
                var a = app;
                menu.Items.Add(Mni("💻", a.DisplayName,
                    () => FileContextMenuService.OpenWith(a.ExePath, item.FullPath)));
            }
            menu.PlacementTarget = BtnPreviewOpenWith;
            menu.Placement = PlacementMode.Top;
            menu.IsOpen = true;
        }

        private void BtnUploadDrive_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileList.SelectedItems.Cast<FileItem>().ToList();
            var paths    = selected.Count > 0
                ? selected.Select(f => f.FullPath).ToList()
                : new List<string> { _currentPath };
            new UploadWindow(paths) { Owner = this }.ShowDialog();
        }

        private void UploadToDrive(List<string> paths)
        {
            var uploadPaths = new List<string>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    var confirm = new UploadConfirmWindow(path) { Owner = this };
                    confirm.ShowDialog();
                    if (!confirm.Confirmed) return;
                    uploadPaths.Add(confirm.UploadPath);
                }
                else uploadPaths.Add(path);
            }
            if (uploadPaths.Count > 0)
                new UploadWindow(uploadPaths) { Owner = this }.ShowDialog();
        }

        // ════════════════════════════════════════════════════════════════════
        //  КОНТЕКСТНОЕ МЕНЮ ПКМ
        // ════════════════════════════════════════════════════════════════════

        private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Up or Key.Down)
                e.Handled = true;
        }

        private void FileList_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    if (FileList.SelectedItems.Contains(fi))
                        e.Handled = true;
                    else
                    {
                        FileList.SelectedItems.Clear();
                        FileList.SelectedItem = fi;
                        e.Handled = true;
                    }
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
        }

        private void FileList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var selected = FileList.SelectedItems.Cast<FileItem>().ToList();
            var item     = selected.Count == 1 ? selected[0] : null;
            if (selected.Count > 1) item = null;

            var menu = BuildContextMenu();

            if (selected.Count > 1)
            {
                menu.Items.Add(Mni("☁", $"Загрузить все на Google Drive ({selected.Count})",
                    () => UploadToDrive(selected.Select(f => f.FullPath).ToList())));
                menu.Items.Add(new Separator());
                menu.Items.Add(Mni("📋", "Копировать пути",
                    () => FileContextMenuService.CopyPath(
                        string.Join(Environment.NewLine, selected.Select(f => f.FullPath)))));
            }
            else if (item != null)
            {
                menu.Items.Add(Mni("▶", "Открыть", () =>
                {
                    if (item.IsDir) Navigate(item.FullPath);
                    else FileContextMenuService.OpenDefault(item.FullPath);
                }));

                var apps = FileContextMenuService.GetOpenWithApps(item.FullPath);
                if (apps.Count > 0)
                {
                    var sub = MniSub("🔧", "Открыть с помощью");
                    foreach (var app in apps)
                    {
                        var a = app;
                        sub.Items.Add(Mni("💻", a.DisplayName,
                            () => FileContextMenuService.OpenWith(a.ExePath, item.FullPath)));
                    }
                    menu.Items.Add(sub);
                }

                menu.Items.Add(new Separator());
                menu.Items.Add(Mni("☁", "Загрузить на Google Drive",
                    () => UploadToDrive(new List<string> { item.FullPath })));
                menu.Items.Add(new Separator());

                var scripts = FileContextMenuService.LoadScripts();
                if (scripts.Count > 0)
                {
                    var subS = MniSub("⚡", "Запустить скрипт");
                    foreach (var s in scripts)
                    {
                        var sc = s;
                        subS.Items.Add(sc.Command == "__copy_path__"
                            ? Mni("📋", sc.Label, () => FileContextMenuService.CopyPath(item.FullPath))
                            : Mni("▶", sc.Label, () => FileContextMenuService.RunScript(sc, item.FullPath)));
                    }
                    menu.Items.Add(subS);
                    menu.Items.Add(new Separator());
                }

                menu.Items.Add(Mni("📂", "Показать в Проводнике",
                    () => FileContextMenuService.RevealInExplorer(item.FullPath)));
                menu.Items.Add(Mni("📋", "Копировать путь",
                    () => FileContextMenuService.CopyPath(item.FullPath)));
            }
            else
            {
                menu.Items.Add(Mni("🖥", "Открыть терминал здесь", () =>
                    FileContextMenuService.RunScript(
                        new ScriptEntry { Command = "powershell.exe",
                            Args = $"-NoExit -Command \"Set-Location '{_currentPath}'\"" },
                        _currentPath)));
                menu.Items.Add(Mni("📋", "Копировать путь папки",
                    () => FileContextMenuService.CopyPath(_currentPath)));
            }

            menu.PlacementTarget = FileList;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static ContextMenu BuildContextMenu()
        {
            var tpl = new ControlTemplate(typeof(ContextMenu));
            var bd  = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty,      new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
            bd.SetValue(Border.BorderBrushProperty,     new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty,         new Thickness(0, 4, 0, 4));
            bd.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            tpl.VisualTree = bd;
            return new ContextMenu { OverridesDefaultStyle = true, Template = tpl };
        }

        private static ControlTemplate BuildMniTemplate()
        {
            var tpl = new ControlTemplate(typeof(MenuItem));
            var bd  = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
            bd.SetValue(Border.PaddingProperty,    new Thickness(12, 6, 24, 6));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty,    "Header");
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var hov = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hov.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)), "Bd"));
            tpl.Triggers.Add(hov);
            var dis = new Trigger { Property = MenuItem.IsEnabledProperty, Value = false };
            dis.Setters.Add(new Setter(MenuItem.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))));
            tpl.Triggers.Add(dis);
            return tpl;
        }

        private static ControlTemplate BuildMniSubTemplate()
        {
            var tpl  = new ControlTemplate(typeof(MenuItem));
            var bd   = new FrameworkElementFactory(typeof(Border));
            bd.Name  = "Bd";
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
            bd.SetValue(Border.PaddingProperty,    new Thickness(12, 6, 24, 6));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var c0   = new FrameworkElementFactory(typeof(ColumnDefinition));
            c0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var c1   = new FrameworkElementFactory(typeof(ColumnDefinition));
            c1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(c0); grid.AppendChild(c1);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(Grid.ColumnProperty, 0);
            cp.SetValue(ContentPresenter.ContentSourceProperty,    "Header");
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(cp);
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(Grid.ColumnProperty, 1);
            arrow.SetValue(TextBlock.TextProperty,       "›");
            arrow.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)));
            arrow.SetValue(TextBlock.FontSizeProperty,   14.0);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            grid.AppendChild(arrow);
            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.SetValue(Popup.PlacementProperty,        PlacementMode.Right);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty,        false);
            popup.SetBinding(Popup.IsOpenProperty,
                new Binding("IsSubmenuOpen") { RelativeSource = RelativeSource.TemplatedParent });
            var popBd = new FrameworkElementFactory(typeof(Border));
            popBd.SetValue(Border.BackgroundProperty,      new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
            popBd.SetValue(Border.BorderBrushProperty,     new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)));
            popBd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popBd.SetValue(Border.PaddingProperty,         new Thickness(0, 4, 0, 4));
            popBd.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            popup.AppendChild(popBd);
            grid.AppendChild(popup);
            bd.AppendChild(grid);
            tpl.VisualTree = bd;
            var hov = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hov.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)), "Bd"));
            tpl.Triggers.Add(hov);
            return tpl;
        }

        private static MenuItem Mni(string icon, string header, Action onClick, bool enabled = true)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = icon, FontSize = 13, Width = 20,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            panel.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
            var m = new MenuItem
            {
                Header = panel, IsEnabled = enabled,
                OverridesDefaultStyle = true,
                Template = BuildMniTemplate(),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };
            m.Click += (_, _) => onClick();
            return m;
        }

        private static MenuItem MniSub(string icon, string header)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = icon, FontSize = 13, Width = 20,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            panel.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
            var m = new MenuItem
            {
                Header = panel,
                OverridesDefaultStyle = true,
                Template = BuildMniSubTemplate(),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };
            m.MouseEnter += (_, _) => m.IsSubmenuOpen = true;
            m.MouseLeave += (_, _) => m.IsSubmenuOpen = false;
            return m;
        }

        // ════════════════════════════════════════════════════════════════════
        //  ФАЙЛОВЫЕ ОПЕРАЦИИ (Ctrl+C / Ctrl+X / Ctrl+V / Delete)
        // ════════════════════════════════════════════════════════════════════

        private List<string>   _clipboard          = new();
        private bool           _clipboardCut       = false;
        private int            _lastArrowDirection = 0;
        private FileItem?      _hoveredItem;
        private PreviewWindow? _previewWindow;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (SearchBox.IsFocused) return;

            if (e.Key == Key.Down)
            {
                _lastArrowDirection = 1;
                var i = FileList.SelectedIndex;
                if (i < FileList.Items.Count - 1) { FileList.SelectedIndex = i + 1; ScrollIfNeeded(i + 1); }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _lastArrowDirection = -1;
                var i = FileList.SelectedIndex;
                if (i > 0) { FileList.SelectedIndex = i - 1; ScrollIfNeeded(i - 1); }
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                var target = (_hoveredItem != null && !_hoveredItem.IsDir)
                    ? _hoveredItem
                    : FileList.SelectedItems.Cast<FileItem>().FirstOrDefault(f => !f.IsDir);
                if (target != null && _previewWindow == null)
                    OpenPreview(target.FullPath);
                e.Handled = true;
            }
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && _previewWindow != null)
            {
                _previewWindow.Close();
                e.Handled = true;
            }
        }

        private void ScrollIfNeeded(int index)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var count = FileList.Items.Count;
                if (index < 0 || index >= count) return;
                var anchor = _lastArrowDirection == 1
                    ? Math.Min(index + 1, count - 1)
                    : Math.Max(index - 1, 0);
                FileList.ScrollIntoView(FileList.Items[anchor]);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OpenPreview(string path)
        {
            _previewWindow = new PreviewWindow(path) { Owner = this };
            _previewWindow.Closed += (_, _) => _previewWindow = null;
            _previewWindow.PreviewKeyUp += (_, e) =>
            {
                if (e.Key == Key.Space) { _previewWindow?.Close(); e.Handled = true; }
            };
            _previewWindow.Show();
        }

        private void AttachHoverTracking()
        {
            FileList.MouseMove  -= FileList_MouseMove;
            FileList.MouseMove  += FileList_MouseMove;
            FileList.MouseLeave -= FileList_MouseLeave;
            FileList.MouseLeave += FileList_MouseLeave;
        }

        private void FileList_MouseMove(object sender, MouseEventArgs e)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is ListViewItem lvi && lvi.Content is FileItem fi)
                {
                    _hoveredItem = fi;
                    if (_previewWindow != null && !fi.IsDir)
                        _previewWindow.NavigateTo(fi.FullPath);
                    return;
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
            _hoveredItem = null;
        }

        private void FileList_MouseLeave(object sender, MouseEventArgs e)
            => _hoveredItem = null;

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (SearchBox.IsFocused) return;

            var selected = FileList.SelectedItems.Cast<FileItem>().ToList();

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && selected.Count > 0)
            {
                ResetCutMarks();
                _clipboard    = selected.Select(f => f.FullPath).ToList();
                _clipboardCut = false;
                e.Handled = true;
            }
            else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control && selected.Count > 0)
            {
                ResetCutMarks();
                _clipboard    = selected.Select(f => f.FullPath).ToList();
                _clipboardCut = true;
                foreach (var f in selected) f.Opacity = 0.35;
                FileList.Items.Refresh();
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && _clipboard.Count > 0)
            {
                PasteFiles();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && selected.Count > 0)
            {
                DeleteFiles(selected);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && selected.Count == 1)
            {
                var item = selected[0];
                if (item.IsDir) Navigate(item.FullPath);
                else FileContextMenuService.OpenDefault(item.FullPath);
                e.Handled = true;
            }
        }

        private void PasteFiles()
        {
            foreach (var src in _clipboard)
            {
                try
                {
                    var name = System.IO.Path.GetFileName(src);
                    var dst  = System.IO.Path.Combine(_currentPath, name);
                    if (File.Exists(dst) || Directory.Exists(dst))
                        dst = GetUniquePath(dst);
                    if (Directory.Exists(src))
                    {
                        CopyDirectory(src, dst);
                        if (_clipboardCut) Directory.Delete(src, recursive: true);
                    }
                    else
                    {
                        File.Copy(src, dst);
                        if (_clipboardCut) File.Delete(src);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка:\n{ex.Message}", "Lambada",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            if (_clipboardCut) _clipboard.Clear();
            ResetCutMarks();
            LoadDirectory(_currentPath);
        }

        private void DeleteFiles(List<FileItem> items)
        {
            var names = items.Count == 1 ? $"\"{items[0].Name}\"" : $"{items.Count} файлов";
            var dlg   = new DeleteConfirmWindow($"Удалить {names}?") { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            var firstIndex = FileList.SelectedIndex;
            _suppressWatcher = true;
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            foreach (var item in items)
            {
                try
                {
                    if (item.IsDir) Directory.Delete(item.FullPath, recursive: true);
                    else           File.Delete(item.FullPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления:\n{ex.Message}", "Lambada",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            _suppressWatcher = false;
            if (_watcher != null) _watcher.EnableRaisingEvents = true;

            var targetIndex = firstIndex;
            LoadDirectory(_currentPath);
            Dispatcher.InvokeAsync(() =>
            {
                if (FileList.Items.Count > 0)
                {
                    var idx = Math.Min(targetIndex, FileList.Items.Count - 1);
                    FileList.SelectedIndex = idx;
                    FileList.ScrollIntoView(FileList.Items[idx]);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ResetCutMarks()
        {
            foreach (var item in _allItems) item.Opacity = 1.0;
            FileList.Items.Refresh();
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(dir)));
        }

        private static string GetUniquePath(string path)
        {
            var dir  = System.IO.Path.GetDirectoryName(path) ?? "";
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var ext  = System.IO.Path.GetExtension(path);
            int i    = 1;
            string result;
            do { result = System.IO.Path.Combine(dir, $"{name} ({i++}){ext}"); }
            while (File.Exists(result) || Directory.Exists(result));
            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        //  ПОИСК
        // ════════════════════════════════════════════════════════════════════

        private void SearchArea_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateSearch(true);
            SearchBox.Focus();
        }

        private void SearchArea_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) return;
            AnimateSearch(false);
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) return;
            AnimateSearch(false);
        }

        private void AnimateSearch(bool open)
        {
            var wa = new DoubleAnimation(open ? 170 : 0, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            SearchBorder.BeginAnimation(WidthProperty, wa);
            SearchBorder.BeginAnimation(OpacityProperty,
                new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(150)));
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter(SearchBox.Text);

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { SearchBox.Text = ""; AnimateSearch(false); }
        }

        // ════════════════════════════════════════════════════════════════════
        //  SIDEBAR
        // ════════════════════════════════════════════════════════════════════

        private void BuildSidebar()
        {
            AddSidebarSection("Quick access");
            AddSidebarItem("🖥", "Desktop",   Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            AddSidebarItem("⬇", "Downloads", System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            AddSidebarItem("📄", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddSidebarItem("🖼", "Pictures",  Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            AddSidebarSection("Drives");
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                AddSidebarItem("💾", $"{drive.Name} ({drive.DriveType})", drive.RootDirectory.FullName);
        }

        private void AddSidebarSection(string title)
        {
            SidebarPanel.Children.Add(new TextBlock
            {
                Text = title.ToUpper(), FontSize = 10, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)),
                Margin = new Thickness(12, 10, 12, 2)
            });
        }

        private void AddSidebarItem(string icon, string label, string path)
        {
            var btn = new Button
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 5, 12, 5), Tag = path
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = icon, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) });
            panel.Children.Add(new TextBlock
            {
                Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85))
            });
            btn.Content    = panel;
            btn.Click      += (_, _) => { if (btn.Tag is string p) Navigate(p); };
            btn.MouseEnter += (_, _) => btn.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
            SidebarPanel.Children.Add(btn);
        }

        // ════════════════════════════════════════════════════════════════════
        //  ХЕЛПЕРЫ
        // ════════════════════════════════════════════════════════════════════

        private static string GetFileIcon(string ext) => ext.ToLower() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "🖼",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv"             => "🎬",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a"            => "🎵",
            ".pdf"                                                       => "📕",
            ".doc" or ".docx"                                            => "📝",
            ".xls" or ".xlsx"                                            => "📊",
            ".ppt" or ".pptx"                                            => "📋",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz"               => "📦",
            ".exe" or ".msi"                                             => "⚙",
            ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c"         => "💻",
            ".txt" or ".md" or ".log"                                    => "📄",
            _                                                            => "📄"
        };

        private static string GetIconColor(string ext) => ext.ToLower() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "#5DCAA5",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv"             => "#AFA9EC",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a"            => "#F0997B",
            ".pdf"                                                       => "#E24B4A",
            ".doc" or ".docx"                                            => "#85B7EB",
            ".zip" or ".rar" or ".7z"                                   => "#FAC775",
            ".cs" or ".py" or ".js" or ".ts"                            => "#62C554",
            _                                                            => "#858585"
        };

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)                return $"{bytes} B";
            if (bytes < 1024 * 1024)         return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        protected override void OnClosed(EventArgs e)
        {
            _watcher?.Dispose();
            base.OnClosed(e);
        }
    }
}
