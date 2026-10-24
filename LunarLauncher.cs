using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LunarLauncher
{
    public class LauncherWindow : Window
    {
        private const string CollectionId = "3320591539";
        private const string CollectionUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3320591539";
        private const string ArkAppId = "346110";

        public struct MapEntry
        {
            public string Number;
            public string Name;
            public int Port;
            public MapEntry(string number, string name, int port)
            {
                Number = number;
                Name = name;
                Port = port;
            }
        }

        private readonly MapEntry[] maps = new MapEntry[]
        {
            new MapEntry("01", "The Island", 15401),
            new MapEntry("02", "The Center", 15411),
            new MapEntry("03", "Ragnarok", 15421),
            new MapEntry("04", "Crystal Isles", 15431),
            new MapEntry("05", "Extinction", 15441),
            new MapEntry("06", "Valguero", 15451),
            new MapEntry("07", "Fjordur", 15461),
            new MapEntry("08", "Lost Island", 15471),
            new MapEntry("09", "Scorched Earth", 15481),
            new MapEntry("10", "Aberration", 15491),
            new MapEntry("11", "Genesis 1", 15501),
            new MapEntry("12", "Genesis 2", 15511),
            new MapEntry("13", "Riftstorm", 15531)
        };

        private string currentClusterIp = "46.4.188.156";

        // Exact palette from Lunar ARK pause_menu_full.png
        private readonly Color colPanelDark = Color.FromRgb(6, 20, 28);        // #06141C
        private readonly Color colTealFill = Color.FromRgb(11, 76, 105);       // #0B4C69
        private readonly Color colTealHover = Color.FromRgb(17, 104, 142);     // #11688E
        private readonly Color colTealIdle = Color.FromArgb(140, 6, 28, 40);
        private readonly Color colBorderActive = Color.FromRgb(102, 143, 161); // #668FA1
        private readonly Color colBorderIdle = Color.FromArgb(140, 65, 100, 115);
        private readonly Color colCyan = Color.FromRgb(17, 165, 185);          // #11A5B9
        private readonly Color colWhite = Color.FromRgb(245, 248, 250);
        private readonly Color colMuted = Color.FromRgb(140, 165, 175);
        private readonly Color colDanger = Color.FromRgb(165, 45, 45);

        private readonly FontFamily mainFont = new FontFamily("Snasm Light, Segoe UI Semibold, Segoe UI");

        private Border btn2ManBorder;
        private Border btn4ManBorder;
        private TextBlock btn2ManText;
        private TextBlock btn4ManText;
        private Polygon btn2ManArrow;
        private Polygon btn4ManArrow;
        private TextBlock activeIpLabel;

        // Bottom-right action buttons & queue status
        private TextBlock btnUpdateModsText;
        private TextBlock btnQueueWipeText;
        private Border queueStatusBanner;
        private TextBlock queueStatusText;

        // Wipe Queue state
        private DispatcherTimer wipeTimer;
        private bool isWipeQueued = false;
        private DateTime wipeTargetTime;
        private string wipeClusterIp = "46.4.188.156";
        private MapEntry wipeMap;

        // Modal overlay container
        private Grid modalOverlay;

        // Mod updater state
        private bool isUpdatingMods = false;
        private CancellationTokenSource modCancelToken;

        public LauncherWindow()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Title = "Lunar ARK Launcher";
            Width = 1280;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanMinimize;
            Background = Brushes.Black;

            wipeMap = maps[2]; // Default to Ragnarok

            Border rootBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(35, 58, 68)),
                BorderThickness = new Thickness(1),
                Background = Brushes.Black
            };

            Grid rootGrid = new Grid();
            rootBorder.Child = rootGrid;
            Content = rootBorder;

            // Load embedded 16:9 Lunar ARK background
            Stream bgStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("bg.jpg");
            if (bgStream != null)
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = bgStream;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                Image bgImage = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Fill
                };
                RenderOptions.SetBitmapScalingMode(bgImage, BitmapScalingMode.HighQuality);
                rootGrid.Children.Add(bgImage);
            }

            // Drag window from background
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && modalOverlay.Visibility != Visibility.Visible)
                {
                    DragMove();
                }
            };

            Canvas canvas = new Canvas();
            rootGrid.Children.Add(canvas);

            // Top-right window controls (Minimize / Close)
            StackPanel winControls = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            Canvas.SetRight(winControls, 22);
            Canvas.SetTop(winControls, 18);

            winControls.Children.Add(CreateWindowControlButton("—", false, () => WindowState = WindowState.Minimized));
            Border closeBtn = CreateWindowControlButton("✕", true, () => Close());
            closeBtn.Margin = new Thickness(8, 0, 0, 0);
            winControls.Children.Add(closeBtn);
            canvas.Children.Add(winControls);

            // Cluster status header
            StackPanel statusHeader = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            Canvas.SetLeft(statusHeader, 82);
            Canvas.SetTop(statusHeader, 294);

            StackPanel clusterCol = new StackPanel();
            clusterCol.Children.Add(new TextBlock
            {
                Text = "Cluster",
                FontFamily = mainFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(colCyan)
            });
            activeIpLabel = new TextBlock
            {
                Text = "2-Man   46.4.188.156",
                FontFamily = mainFont,
                FontSize = 22,
                Foreground = new SolidColorBrush(colWhite),
                Margin = new Thickness(0, 2, 0, 0)
            };
            clusterCol.Children.Add(activeIpLabel);
            statusHeader.Children.Add(clusterCol);
            canvas.Children.Add(statusHeader);

            // Cluster selector buttons
            btn2ManBorder = CreateClusterButton("2-Man", "46.4.188.156", out btn2ManText, out btn2ManArrow);
            Canvas.SetLeft(btn2ManBorder, 80);
            Canvas.SetTop(btn2ManBorder, 352);
            canvas.Children.Add(btn2ManBorder);

            btn4ManBorder = CreateClusterButton("4-Man", "5.9.117.144", out btn4ManText, out btn4ManArrow);
            Canvas.SetLeft(btn4ManBorder, 286);
            Canvas.SetTop(btn4ManBorder, 352);
            canvas.Children.Add(btn4ManBorder);

            // 13 Map Buttons in 2 clean columns
            double startY = 420;
            double rowHeight = 38;
            double col1X = 80;
            double col2X = 365;

            for (int i = 0; i < maps.Length; i++)
            {
                int col = (i < 7) ? 0 : 1;
                int row = (i < 7) ? i : (i - 7);

                Border mapItem = CreateMapButton(maps[i]);
                Canvas.SetLeft(mapItem, col == 0 ? col1X : col2X);
                Canvas.SetTop(mapItem, startY + row * rowHeight);
                canvas.Children.Add(mapItem);
            }

            // Bottom-right Queue Status Banner (shown when a wipe queue is active)
            queueStatusBanner = CreateQueueStatusBanner();
            Canvas.SetLeft(queueStatusBanner, 770);
            Canvas.SetTop(queueStatusBanner, 558);
            canvas.Children.Add(queueStatusBanner);

            // Bottom-right 2 buttons matching pause_menu_full.png (X=770..1200, Y=603, H=38)
            Border btnUpdateMods = CreateBottomActionButton("Update Mods", 208, 38, out btnUpdateModsText, OpenModUpdateDialog);
            Canvas.SetLeft(btnUpdateMods, 770);
            Canvas.SetTop(btnUpdateMods, 603);
            canvas.Children.Add(btnUpdateMods);

            Border btnQueueWipe = CreateBottomActionButton("Queue for Wipe", 208, 38, out btnQueueWipeText, OpenWipeQueueDialog);
            Canvas.SetLeft(btnQueueWipe, 992);
            Canvas.SetTop(btnQueueWipe, 603);
            canvas.Children.Add(btnQueueWipe);

            // Modal overlay for dialogs
            modalOverlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(195, 0, 5, 8)),
                Visibility = Visibility.Collapsed
            };
            rootGrid.Children.Add(modalOverlay);

            // Wipe countdown timer (100ms tick for exact second join)
            wipeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            wipeTimer.Tick += OnWipeTimerTick;

            UpdateClusterVisuals();
        }

        private Border CreateBottomActionButton(string text, double width, double height, out TextBlock textBlockOut, Action onClick)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = mainFont,
                FontSize = 16,
                Foreground = new SolidColorBrush(colWhite),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border b = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(colTealFill),
                BorderBrush = new SolidColorBrush(colBorderActive),
                BorderThickness = new Thickness(1.2),
                Cursor = Cursors.Hand,
                Child = tb
            };

            b.MouseEnter += (s, e) =>
            {
                b.Background = new SolidColorBrush(colTealHover);
                b.BorderBrush = new SolidColorBrush(colWhite);
            };
            b.MouseLeave += (s, e) =>
            {
                b.Background = new SolidColorBrush(colTealFill);
                b.BorderBrush = new SolidColorBrush(colBorderActive);
            };
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                onClick();
            };

            textBlockOut = tb;
            return b;
        }

        private Border CreateQueueStatusBanner()
        {
            Grid g = new Grid
            {
                Margin = new Thickness(12, 0, 10, 0)
            };

            queueStatusText = new TextBlock
            {
                Text = "",
                FontFamily = mainFont,
                FontSize = 13.5,
                Foreground = new SolidColorBrush(colCyan),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            g.Children.Add(queueStatusText);

            TextBlock cancelBtn = new TextBlock
            {
                Text = "CANCEL ✕",
                FontFamily = mainFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 110, 110)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand
            };
            cancelBtn.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                CancelWipeQueue();
            };
            g.Children.Add(cancelBtn);

            Border b = new Border
            {
                Width = 430,
                Height = 34,
                Background = new SolidColorBrush(Color.FromArgb(220, 6, 22, 30)),
                BorderBrush = new SolidColorBrush(colCyan),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed,
                Child = g
            };
            return b;
        }

        // =====================================================================
        // WIPE QUEUE DIALOG & TIMER LOGIC
        // =====================================================================
        private void OpenWipeQueueDialog()
        {
            modalOverlay.Children.Clear();
            modalOverlay.Visibility = Visibility.Visible;

            string selectedClusterIp = isWipeQueued ? wipeClusterIp : currentClusterIp;
            MapEntry selectedMap = isWipeQueued ? wipeMap : maps[2];

            DateTime defaultTime = isWipeQueued
                ? wipeTargetTime
                : DateTime.Now.AddHours(1).Date.AddHours(DateTime.Now.AddHours(1).Hour);

            int selHour = defaultTime.Hour;
            int selMin = defaultTime.Minute;
            int selSec = defaultTime.Second;

            Border dialog = new Border
            {
                Width = 640,
                Height = 465,
                Background = new SolidColorBrush(colPanelDark),
                BorderBrush = new SolidColorBrush(colBorderActive),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas c = new Canvas();
            dialog.Child = c;

            // Header
            TextBlock title = new TextBlock
            {
                Text = "QUEUE FOR WIPE",
                FontFamily = mainFont,
                FontSize = 24,
                Foreground = new SolidColorBrush(colWhite)
            };
            Canvas.SetLeft(title, 28);
            Canvas.SetTop(title, 22);
            c.Children.Add(title);

            TextBlock subTitle = new TextBlock
            {
                Text = "Automatically starts ARK and joins the selected server at the exact target time.",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                Foreground = new SolidColorBrush(colMuted)
            };
            Canvas.SetLeft(subTitle, 28);
            Canvas.SetTop(subTitle, 52);
            c.Children.Add(subTitle);

            Border closeX = CreateWindowControlButton("✕", true, CloseModal);
            Canvas.SetRight(closeX, 20);
            Canvas.SetTop(closeX, 20);
            c.Children.Add(closeX);

            // 1. Cluster Selection
            TextBlock lblCluster = new TextBlock
            {
                Text = "01   SELECT CLUSTER",
                FontFamily = mainFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(colCyan)
            };
            Canvas.SetLeft(lblCluster, 28);
            Canvas.SetTop(lblCluster, 88);
            c.Children.Add(lblCluster);

            Border c2Man = null, c4Man = null;
            Action updateClusterBtns = () =>
            {
                bool is2 = selectedClusterIp == "46.4.188.156";
                c2Man.Background = new SolidColorBrush(is2 ? colTealFill : colTealIdle);
                c2Man.BorderBrush = new SolidColorBrush(is2 ? colBorderActive : colBorderIdle);
                c4Man.Background = new SolidColorBrush(!is2 ? colTealFill : colTealIdle);
                c4Man.BorderBrush = new SolidColorBrush(!is2 ? colBorderActive : colBorderIdle);
            };

            c2Man = CreateDialogOptionButton("2-Man  (46.4.188.156)", 284, 36, () =>
            {
                selectedClusterIp = "46.4.188.156";
                updateClusterBtns();
            });
            Canvas.SetLeft(c2Man, 28);
            Canvas.SetTop(c2Man, 110);
            c.Children.Add(c2Man);

            c4Man = CreateDialogOptionButton("4-Man  (5.9.117.144)", 284, 36, () =>
            {
                selectedClusterIp = "5.9.117.144";
                updateClusterBtns();
            });
            Canvas.SetLeft(c4Man, 326);
            Canvas.SetTop(c4Man, 110);
            c.Children.Add(c4Man);
            updateClusterBtns();

            // 2. Map Selection Grid (13 maps)
            TextBlock lblMap = new TextBlock
            {
                Text = "02   SELECT MAP",
                FontFamily = mainFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(colCyan)
            };
            Canvas.SetLeft(lblMap, 28);
            Canvas.SetTop(lblMap, 162);
            c.Children.Add(lblMap);

            List<Border> mapBorders = new List<Border>();
            Action updateMapBtns = () =>
            {
                for (int idx = 0; idx < maps.Length; idx++)
                {
                    bool active = maps[idx].Port == selectedMap.Port;
                    mapBorders[idx].Background = new SolidColorBrush(active ? colTealFill : colTealIdle);
                    mapBorders[idx].BorderBrush = new SolidColorBrush(active ? colBorderActive : colBorderIdle);
                }
            };

            for (int i = 0; i < maps.Length; i++)
            {
                int col = i % 3;
                int row = i / 3;
                MapEntry m = maps[i];

                Border mb = CreateDialogOptionButton(m.Number + "  " + m.Name, 188, 30, () =>
                {
                    selectedMap = m;
                    updateMapBtns();
                });
                Canvas.SetLeft(mb, 28 + col * 197);
                Canvas.SetTop(mb, 184 + row * 35);
                mapBorders.Add(mb);
                c.Children.Add(mb);
            }
            updateMapBtns();

            // 3. Target Time Picker (HH : MM : SS)
            TextBlock lblTime = new TextBlock
            {
                Text = "03   TARGET LAUNCH TIME (24H)",
                FontFamily = mainFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(colCyan)
            };
            Canvas.SetLeft(lblTime, 28);
            Canvas.SetTop(lblTime, 372);
            c.Children.Add(lblTime);

            TextBox tbHour, tbMin, tbSec;
            Border spinnerHour = CreateTimeSpinner(selHour, 23, out tbHour);
            Canvas.SetLeft(spinnerHour, 28);
            Canvas.SetTop(spinnerHour, 396);
            c.Children.Add(spinnerHour);

            TextBlock sep1 = new TextBlock
            {
                Text = ":",
                FontFamily = mainFont,
                FontSize = 22,
                Foreground = new SolidColorBrush(colWhite)
            };
            Canvas.SetLeft(sep1, 122);
            Canvas.SetTop(sep1, 400);
            c.Children.Add(sep1);

            Border spinnerMin = CreateTimeSpinner(selMin, 59, out tbMin);
            Canvas.SetLeft(spinnerMin, 134);
            Canvas.SetTop(spinnerMin, 396);
            c.Children.Add(spinnerMin);

            TextBlock sep2 = new TextBlock
            {
                Text = ":",
                FontFamily = mainFont,
                FontSize = 22,
                Foreground = new SolidColorBrush(colWhite)
            };
            Canvas.SetLeft(sep2, 228);
            Canvas.SetTop(sep2, 400);
            c.Children.Add(sep2);

            Border spinnerSec = CreateTimeSpinner(selSec, 59, out tbSec);
            Canvas.SetLeft(spinnerSec, 240);
            Canvas.SetTop(spinnerSec, 396);
            c.Children.Add(spinnerSec);

            // Action buttons at bottom-right of dialog
            TextBlock dummy;
            if (isWipeQueued)
            {
                Border stopBtn = CreateBottomActionButton("Cancel Queue", 135, 36, out dummy, () =>
                {
                    CancelWipeQueue();
                    CloseModal();
                });
                stopBtn.Background = new SolidColorBrush(colDanger);
                Canvas.SetLeft(stopBtn, 338);
                Canvas.SetTop(stopBtn, 396);
                c.Children.Add(stopBtn);
            }

            Border startQueueBtn = CreateBottomActionButton("Start Queue  ►", 168, 36, out dummy, () =>
            {
                int h = ParseClamped(tbHour.Text, 0, 23);
                int m = ParseClamped(tbMin.Text, 0, 59);
                int s = ParseClamped(tbSec.Text, 0, 59);

                DateTime now = DateTime.Now;
                DateTime target = new DateTime(now.Year, now.Month, now.Day, h, m, s);
                if (target <= now)
                {
                    target = target.AddDays(1);
                }

                wipeClusterIp = selectedClusterIp;
                wipeMap = selectedMap;
                wipeTargetTime = target;
                isWipeQueued = true;

                currentClusterIp = selectedClusterIp;
                UpdateClusterVisuals();

                wipeTimer.Start();
                UpdateWipeQueueUI();
                CloseModal();
            });
            Canvas.SetLeft(startQueueBtn, 444);
            Canvas.SetTop(startQueueBtn, 396);
            c.Children.Add(startQueueBtn);

            modalOverlay.Children.Add(dialog);
        }

        private Border CreateDialogOptionButton(string text, double w, double h, Action onClick)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontFamily = mainFont,
                FontSize = 14.5,
                Foreground = new SolidColorBrush(colWhite),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };

            Border b = new Border
            {
                Width = w,
                Height = h,
                Background = new SolidColorBrush(colTealIdle),
                BorderBrush = new SolidColorBrush(colBorderIdle),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = tb
            };

            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                onClick();
            };

            return b;
        }

        private Border CreateTimeSpinner(int initialVal, int maxVal, out TextBox tbOut)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            TextBox tb = new TextBox
            {
                Text = initialVal.ToString("D2"),
                FontFamily = mainFont,
                FontSize = 18,
                Foreground = new SolidColorBrush(colWhite),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                CaretBrush = new SolidColorBrush(colCyan),
                MaxLength = 2
            };
            Grid.SetColumn(tb, 1);

            Border btnMinus = CreateSmallStepButton("-", () =>
            {
                int v = ParseClamped(tb.Text, 0, maxVal);
                v = (v - 1 + maxVal + 1) % (maxVal + 1);
                tb.Text = v.ToString("D2");
            });
            Grid.SetColumn(btnMinus, 0);

            Border btnPlus = CreateSmallStepButton("+", () =>
            {
                int v = ParseClamped(tb.Text, 0, maxVal);
                v = (v + 1) % (maxVal + 1);
                tb.Text = v.ToString("D2");
            });
            Grid.SetColumn(btnPlus, 2);

            g.Children.Add(btnMinus);
            g.Children.Add(tb);
            g.Children.Add(btnPlus);

            Border wrap = new Border
            {
                Width = 90,
                Height = 36,
                Background = new SolidColorBrush(colTealIdle),
                BorderBrush = new SolidColorBrush(colBorderActive),
                BorderThickness = new Thickness(1),
                Child = g
            };

            tbOut = tb;
            return wrap;
        }

        private Border CreateSmallStepButton(string txt, Action onClick)
        {
            TextBlock t = new TextBlock
            {
                Text = txt,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 15,
                Foreground = new SolidColorBrush(colCyan),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Border b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(90, 11, 76, 105)),
                Cursor = Cursors.Hand,
                Child = t
            };
            b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(colTealFill);
            b.MouseLeave += (s, e) => b.Background = new SolidColorBrush(Color.FromArgb(90, 11, 76, 105));
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                onClick();
            };
            return b;
        }

        private int ParseClamped(string s, int min, int max)
        {
            int v;
            if (!int.TryParse(s, out v)) return min;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void OnWipeTimerTick(object sender, EventArgs e)
        {
            if (!isWipeQueued)
            {
                wipeTimer.Stop();
                return;
            }

            TimeSpan rem = wipeTargetTime - DateTime.Now;
            if (rem.TotalMilliseconds <= 0)
            {
                // Target reached! Launch ARK and connect immediately
                isWipeQueued = false;
                wipeTimer.Stop();
                UpdateWipeQueueUI();
                ConnectToServerIpPort(wipeClusterIp, wipeMap.Port);
                return;
            }

            UpdateWipeQueueUI();
        }

        private void UpdateWipeQueueUI()
        {
            if (!isWipeQueued)
            {
                queueStatusBanner.Visibility = Visibility.Collapsed;
                btnQueueWipeText.Text = "Queue for Wipe";
                return;
            }

            TimeSpan rem = wipeTargetTime - DateTime.Now;
            if (rem.TotalSeconds < 0) rem = TimeSpan.Zero;

            string remStr = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)rem.TotalHours, rem.Minutes, rem.Seconds);
            string clusterName = (wipeClusterIp == "46.4.188.156") ? "2-Man" : "4-Man";

            queueStatusBanner.Visibility = Visibility.Visible;
            queueStatusText.Text = string.Format("QUEUE  {0} • {1} @ {2:HH:mm:ss}  ({3})",
                clusterName, wipeMap.Name, wipeTargetTime, remStr);
            btnQueueWipeText.Text = "Queued  " + remStr;
        }

        private void CancelWipeQueue()
        {
            isWipeQueued = false;
            wipeTimer.Stop();
            UpdateWipeQueueUI();
        }

        // =====================================================================
        // MOD UPDATER DIALOG & WORKSHOP DOWNLOADER / EXTRACTOR
        // =====================================================================
        private void OpenModUpdateDialog()
        {
            modalOverlay.Children.Clear();
            modalOverlay.Visibility = Visibility.Visible;

            Border dialog = new Border
            {
                Width = 640,
                Height = 400,
                Background = new SolidColorBrush(colPanelDark),
                BorderBrush = new SolidColorBrush(colBorderActive),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas c = new Canvas();
            dialog.Child = c;

            TextBlock title = new TextBlock
            {
                Text = "LUNAR ARK MOD UPDATER",
                FontFamily = mainFont,
                FontSize = 24,
                Foreground = new SolidColorBrush(colWhite)
            };
            Canvas.SetLeft(title, 28);
            Canvas.SetTop(title, 22);
            c.Children.Add(title);

            TextBlock subTitle = new TextBlock
            {
                Text = "Collection #3320591539 — Automatically downloads and unpacks all cluster mods.",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                Foreground = new SolidColorBrush(colMuted)
            };
            Canvas.SetLeft(subTitle, 28);
            Canvas.SetTop(subTitle, 52);
            c.Children.Add(subTitle);

            Border closeX = CreateWindowControlButton("✕", true, () =>
            {
                if (isUpdatingMods && modCancelToken != null)
                {
                    modCancelToken.Cancel();
                }
                CloseModal();
            });
            Canvas.SetRight(closeX, 20);
            Canvas.SetTop(closeX, 20);
            c.Children.Add(closeX);

            string arkLibraryPath = DetectArkSteamLibraryPath();
            string arkModsDir = arkLibraryPath != null
                ? System.IO.Path.Combine(arkLibraryPath, @"steamapps\common\ARK\ShooterGame\Content\Mods")
                : "Not found automatically";

            TextBlock lblPathHeader = new TextBlock
            {
                Text = "DETECTED ARK MODS PATH",
                FontFamily = mainFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(colCyan)
            };
            Canvas.SetLeft(lblPathHeader, 28);
            Canvas.SetTop(lblPathHeader, 92);
            c.Children.Add(lblPathHeader);

            TextBlock lblPathVal = new TextBlock
            {
                Text = arkModsDir,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 12.5,
                Foreground = new SolidColorBrush(colWhite)
            };
            Canvas.SetLeft(lblPathVal, 28);
            Canvas.SetTop(lblPathVal, 114);
            c.Children.Add(lblPathVal);

            // Progress status & bar
            TextBlock statusLabel = new TextBlock
            {
                Text = "Ready to check & update all mods from Collection #3320591539.",
                FontFamily = mainFont,
                FontSize = 14,
                Width = 584,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(colWhite)
            };
            Canvas.SetLeft(statusLabel, 28);
            Canvas.SetTop(statusLabel, 165);
            c.Children.Add(statusLabel);

            TextBlock detailLabel = new TextBlock
            {
                Text = "Click 'Update All Mods' below to start.",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                Width = 584,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(colMuted)
            };
            Canvas.SetLeft(detailLabel, 28);
            Canvas.SetTop(detailLabel, 192);
            c.Children.Add(detailLabel);

            Border progBg = new Border
            {
                Width = 584,
                Height = 22,
                Background = new SolidColorBrush(colTealIdle),
                BorderBrush = new SolidColorBrush(colBorderActive),
                BorderThickness = new Thickness(1)
            };
            Canvas.SetLeft(progBg, 28);
            Canvas.SetTop(progBg, 224);

            Border progFill = new Border
            {
                Width = 0,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(colCyan)
            };
            progBg.Child = progFill;
            c.Children.Add(progBg);

            // Force reinstall checkbox option
            bool forceAll = false;
            Border forceToggle = null;
            TextBlock forceText = new TextBlock
            {
                Text = "[  ]  Force re-download & unpack all mods",
                FontFamily = mainFont,
                FontSize = 13.5,
                Foreground = new SolidColorBrush(colMuted),
                VerticalAlignment = VerticalAlignment.Center
            };
            forceToggle = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = forceText
            };
            forceToggle.MouseLeftButtonDown += (s, e) =>
            {
                forceAll = !forceAll;
                forceText.Text = forceAll
                    ? "[✓]  Force re-download & unpack all mods"
                    : "[  ]  Force re-download & unpack all mods";
                forceText.Foreground = new SolidColorBrush(forceAll ? colCyan : colMuted);
            };
            Canvas.SetLeft(forceToggle, 28);
            Canvas.SetTop(forceToggle, 262);
            c.Children.Add(forceToggle);

            // Bottom buttons: Open Steam Workshop page + Start Update
            TextBlock openWsText;
            Border btnOpenWs = CreateBottomActionButton("Workshop Page", 195, 38, out openWsText, () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "steam://url/CommunityFilePage/" + CollectionId,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    Process.Start(new ProcessStartInfo { FileName = CollectionUrl, UseShellExecute = true });
                }
            });
            btnOpenWs.Background = new SolidColorBrush(colTealIdle);
            Canvas.SetLeft(btnOpenWs, 28);
            Canvas.SetTop(btnOpenWs, 332);
            c.Children.Add(btnOpenWs);

            TextBlock startBtnText = null;
            Border btnStart = null;
            btnStart = CreateBottomActionButton("Update All Mods  ►", 235, 38, out startBtnText, () =>
            {
                if (isUpdatingMods) return;
                if (arkLibraryPath == null)
                {
                    statusLabel.Text = "Could not locate ARK installation folder automatically.";
                    return;
                }

                isUpdatingMods = true;
                startBtnText.Text = "Updating...";
                modCancelToken = new CancellationTokenSource();
                CancellationToken ct = modCancelToken.Token;

                Task.Run(() => RunModUpdateProcess(arkLibraryPath, forceAll, ct, (msg, sub, pct) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        statusLabel.Text = msg;
                        detailLabel.Text = sub;
                        progFill.Width = Math.Max(0, Math.Min(582, 582 * pct));
                        btnUpdateModsText.Text = pct < 1.0 && isUpdatingMods
                            ? string.Format("Updating ({0}%)", (int)(pct * 100))
                            : "Update Mods";
                    }));
                })).ContinueWith(t =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        isUpdatingMods = false;
                        btnUpdateModsText.Text = "Update Mods";
                        startBtnText.Text = "Update All Mods  ►";
                    }));
                });
            });
            Canvas.SetLeft(btnStart, 377);
            Canvas.SetTop(btnStart, 332);
            c.Children.Add(btnStart);

            modalOverlay.Children.Add(dialog);
        }

        private class ModDetails
        {
            public string Id;
            public string Title;
            public long TimeUpdated;
        }

        private void RunModUpdateProcess(string arkLibraryPath, bool forceAll, CancellationToken ct, Action<string, string, double> report)
        {
            try
            {
                report("Fetching collection #3320591539 from Steam API...", "Querying ISteamRemoteStorage/GetCollectionDetails...", 0.02);

                List<string> modIds = FetchCollectionModIds(CollectionId);
                if (modIds.Count == 0)
                {
                    report("Failed to retrieve mod list from Steam API.", "Check your internet connection and try again.", 0.0);
                    return;
                }

                report(string.Format("Found {0} mods in Lunar ARK Collection.", modIds.Count), "Fetching mod metadata & timestamps...", 0.05);
                List<ModDetails> details = FetchPublishedFileDetails(modIds);

                string modsInstallDir = System.IO.Path.Combine(arkLibraryPath, @"steamapps\common\ARK\ShooterGame\Content\Mods");
                Directory.CreateDirectory(modsInstallDir);

                List<ModDetails> toUpdate = new List<ModDetails>();
                foreach (var m in details)
                {
                    string modFile = System.IO.Path.Combine(modsInstallDir, m.Id + ".mod");
                    string modFolder = System.IO.Path.Combine(modsInstallDir, m.Id);
                    if (forceAll || !File.Exists(modFile) || !Directory.Exists(modFolder))
                    {
                        toUpdate.Add(m);
                    }
                    else
                    {
                        DateTime localWriteUtc = File.GetLastWriteTimeUtc(modFile);
                        DateTime remoteUpdateUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(m.TimeUpdated);
                        if (remoteUpdateUtc > localWriteUtc.AddMinutes(1))
                        {
                            toUpdate.Add(m);
                        }
                    }
                }

                if (toUpdate.Count == 0)
                {
                    report(string.Format("All {0} mods are already installed and up to date!", details.Count),
                        "Check 'Force re-download' if you want to verify all mod files.", 1.0);
                    return;
                }

                // Ensure SteamCMD is available to download Workshop items cleanly
                string appDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LunarLauncher", "steamcmd");
                Directory.CreateDirectory(appDataDir);
                string steamCmdExe = System.IO.Path.Combine(appDataDir, "steamcmd.exe");

                if (!File.Exists(steamCmdExe))
                {
                    report("Downloading SteamCMD utility (2.5 MB)...", "First-time setup from steamcdn-a.akamaihd.net...", 0.08);
                    string zipPath = System.IO.Path.Combine(appDataDir, "steamcmd.zip");
                    using (WebClient wc = new WebClient())
                    {
                        wc.DownloadFile("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", zipPath);
                    }
                    ExtractZipFile(zipPath, appDataDir);
                    File.Delete(zipPath);
                }

                int total = toUpdate.Count;
                for (int i = 0; i < total; i++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        report("Mod update cancelled.", "Stopped by user.", 0.0);
                        return;
                    }

                    ModDetails mod = toUpdate[i];
                    double basePct = 0.10 + (0.90 * i / total);
                    report(string.Format("[{0}/{1}] Downloading {2} ({3})...", i + 1, total, mod.Title, mod.Id),
                        "Downloading latest files via Steam Workshop...", basePct);

                    // Check if Steam client already downloaded the latest version in arkLibraryPath
                    string steamWorkshopDir = System.IO.Path.Combine(arkLibraryPath, @"steamapps\workshop\content\346110", mod.Id, "WindowsNoEditor");
                    string cmdWorkshopDir = System.IO.Path.Combine(appDataDir, @"steamapps\workshop\content\346110", mod.Id, "WindowsNoEditor");

                    string activeWorkshopDir = null;
                    if (!forceAll && Directory.Exists(steamWorkshopDir) && File.Exists(System.IO.Path.Combine(steamWorkshopDir, "mod.info")))
                    {
                        DateTime wsTime = File.GetLastWriteTimeUtc(System.IO.Path.Combine(steamWorkshopDir, "mod.info"));
                        DateTime remoteUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(mod.TimeUpdated);
                        if (wsTime >= remoteUtc.AddMinutes(-5))
                        {
                            activeWorkshopDir = steamWorkshopDir;
                        }
                    }

                    if (activeWorkshopDir == null)
                    {
                        string args = string.Format("+login anonymous +workshop_download_item {0} {1} +quit", ArkAppId, mod.Id);
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = steamCmdExe,
                            Arguments = args,
                            WorkingDirectory = appDataDir,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (Process p = Process.Start(psi))
                        {
                            while (!p.WaitForExit(250))
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    try { p.Kill(); } catch { }
                                    report("Mod update cancelled.", "Stopped by user.", 0.0);
                                    return;
                                }
                            }
                        }

                        if (Directory.Exists(cmdWorkshopDir))
                        {
                            activeWorkshopDir = cmdWorkshopDir;
                        }
                        else if (Directory.Exists(steamWorkshopDir))
                        {
                            activeWorkshopDir = steamWorkshopDir;
                        }
                    }

                    if (activeWorkshopDir != null && Directory.Exists(activeWorkshopDir))
                    {
                        double unpackPct = 0.10 + (0.90 * (i + 0.5) / total);
                        report(string.Format("[{0}/{1}] Unpacking & installing {2}...", i + 1, total, mod.Title),
                            "Decompressing .z assets and generating .mod file...", unpackPct);

                        InstallUnpackedArkMod(mod.Id, mod.Title, activeWorkshopDir, modsInstallDir, ct);
                    }
                }

                report(string.Format("All {0} Lunar ARK mods are updated and ready!", details.Count),
                    "You can now join any server without waiting on in-game mod downloads.", 1.0);
            }
            catch (Exception ex)
            {
                report("Error during mod update: " + ex.Message, "Please try again or check your disk space.", 0.0);
            }
        }

        private List<string> FetchCollectionModIds(string collectionId)
        {
            List<string> ids = new List<string>();
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                string body = "collectioncount=1&publishedfileids%5B0%5D=" + collectionId;
                string json = wc.UploadString("https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", "POST", body);

                MatchCollection matches = Regex.Matches(json, "\"publishedfileid\"\\s*:\\s*\"(\\d+)\"");
                foreach (Match m in matches)
                {
                    string id = m.Groups[1].Value;
                    if (id != collectionId && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }
            return ids;
        }

        private List<ModDetails> FetchPublishedFileDetails(List<string> modIds)
        {
            List<ModDetails> list = new List<ModDetails>();
            StringBuilder sb = new StringBuilder();
            sb.Append("itemcount=").Append(modIds.Count);
            for (int i = 0; i < modIds.Count; i++)
            {
                sb.Append("&publishedfileids%5B").Append(i).Append("%5D=").Append(modIds[i]);
            }

            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                string json = wc.UploadString("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", "POST", sb.ToString());

                foreach (string id in modIds)
                {
                    ModDetails d = new ModDetails { Id = id, Title = "Mod " + id, TimeUpdated = 0 };
                    int idx = json.IndexOf("\"publishedfileid\":\"" + id + "\"", StringComparison.Ordinal);
                    if (idx < 0) idx = json.IndexOf("\"publishedfileid\": \"" + id + "\"", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        string slice = json.Substring(idx, Math.Min(2500, json.Length - idx));
                        Match tm = Regex.Match(slice, "\"title\"\\s*:\\s*\"([^\"]+)\"");
                        if (tm.Success) d.Title = tm.Groups[1].Value;

                        Match um = Regex.Match(slice, "\"time_updated\"\\s*:\\s*(\\d+)");
                        if (um.Success) long.TryParse(um.Groups[1].Value, out d.TimeUpdated);
                    }
                    list.Add(d);
                }
            }
            return list;
        }

        private void InstallUnpackedArkMod(string modId, string modTitle, string workshopWindowsNoEditorDir, string arkModsDir, CancellationToken ct)
        {
            string targetModDir = System.IO.Path.Combine(arkModsDir, modId);
            Directory.CreateDirectory(targetModDir);

            // Copy normal files and decompress .z files
            string[] allFiles = Directory.GetFiles(workshopWindowsNoEditorDir, "*", SearchOption.AllDirectories);
            foreach (string srcFile in allFiles)
            {
                if (ct.IsCancellationRequested) return;
                if (srcFile.EndsWith(".uncompressed_size", StringComparison.OrdinalIgnoreCase)) continue;

                string relPath = srcFile.Substring(workshopWindowsNoEditorDir.Length).TrimStart('\\', '/');
                if (srcFile.EndsWith(".z", StringComparison.OrdinalIgnoreCase))
                {
                    string dstFile = System.IO.Path.Combine(targetModDir, relPath.Substring(0, relPath.Length - 2));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dstFile));
                    DecompressUnrealZFile(srcFile, dstFile);
                }
                else
                {
                    string dstFile = System.IO.Path.Combine(targetModDir, relPath);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dstFile));
                    File.Copy(srcFile, dstFile, true);
                }
            }

            // Write <ModId>.mod descriptor file in ShooterGame\Content\Mods
            string modInfoPath = System.IO.Path.Combine(workshopWindowsNoEditorDir, "mod.info");
            string modMetaPath = System.IO.Path.Combine(workshopWindowsNoEditorDir, "modmeta.info");
            string dotModPath = System.IO.Path.Combine(arkModsDir, modId + ".mod");

            if (File.Exists(modInfoPath))
            {
                WriteArkDotModFile(ulong.Parse(modId), modTitle, modInfoPath, modMetaPath, dotModPath);
            }
        }

        private void DecompressUnrealZFile(string srcZPath, string dstPath)
        {
            using (FileStream fsIn = File.OpenRead(srcZPath))
            using (BinaryReader br = new BinaryReader(fsIn))
            using (FileStream fsOut = File.Create(dstPath))
            {
                ulong magic = br.ReadUInt64();
                if (magic != 0x9E2A83C1UL) return;

                ulong chunkSize = br.ReadUInt64();
                ulong totCompSize = br.ReadUInt64();
                ulong totUncompSize = br.ReadUInt64();

                ulong numChunks = (totUncompSize + chunkSize - 1) / chunkSize;
                ulong[] compSizes = new ulong[numChunks];
                for (ulong i = 0; i < numChunks; i++)
                {
                    compSizes[i] = br.ReadUInt64();
                    br.ReadUInt64(); // uncompSize
                }

                byte[] copyBuf = new byte[65536];
                for (ulong i = 0; i < numChunks; i++)
                {
                    byte[] compChunk = br.ReadBytes((int)compSizes[i]);
                    if (compChunk.Length > 2)
                    {
                        // Skip 2-byte zlib header (0x78 0x9C)
                        using (MemoryStream ms = new MemoryStream(compChunk, 2, compChunk.Length - 2))
                        using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                        {
                            int read;
                            while ((read = ds.Read(copyBuf, 0, copyBuf.Length)) > 0)
                            {
                                fsOut.Write(copyBuf, 0, read);
                            }
                        }
                    }
                }
            }
        }

        private void WriteArkDotModFile(ulong modId, string modTitle, string modInfoPath, string modMetaPath, string dstDotModPath)
        {
            byte[] infoBytes = File.ReadAllBytes(modInfoPath);
            using (FileStream fs = File.Create(dstDotModPath))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write(modId);
                WriteUnrealString(bw, modTitle);
                WriteUnrealString(bw, "../../../ShooterGame/Content/Mods/" + modId);

                // Extract map count + map names from mod.info (after first string, excluding trailing 8-byte timestamp)
                int firstStrLen = BitConverter.ToInt32(infoBytes, 0);
                int mapsOffset = 4 + firstStrLen;
                int mapsLen = infoBytes.Length - 8 - mapsOffset;
                if (mapsLen > 0)
                {
                    bw.Write(infoBytes, mapsOffset, mapsLen);
                }
                else
                {
                    bw.Write((int)0);
                }

                bw.Write((uint)0xFF22FF33);
                bw.Write((int)2);

                if (File.Exists(modMetaPath))
                {
                    bw.Write((byte)1);
                    byte[] metaBytes = File.ReadAllBytes(modMetaPath);
                    bw.Write(metaBytes);
                }
                else
                {
                    bw.Write((byte)0);
                    bw.Write((int)0);
                }
            }
        }

        private void WriteUnrealString(BinaryWriter bw, string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                bw.Write((int)0);
                return;
            }
            byte[] bytes = Encoding.ASCII.GetBytes(s);
            bw.Write((int)(bytes.Length + 1));
            bw.Write(bytes);
            bw.Write((byte)0);
        }

        private void ExtractZipFile(string zipPath, string destDir)
        {
            // Use PowerShell Expand-Archive or Shell.Application for net40 compatibility without extra refs
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = string.Format("-NoProfile -Command \"Expand-Archive -Path '{0}' -DestinationPath '{1}' -Force\"", zipPath, destDir),
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
            }
        }

        private string DetectArkSteamLibraryPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string steamPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(steamPath))
                        {
                            steamPath = steamPath.Replace('/', '\\');
                            if (Directory.Exists(System.IO.Path.Combine(steamPath, @"steamapps\common\ARK")))
                            {
                                return steamPath;
                            }

                            string vdf = System.IO.Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
                            if (File.Exists(vdf))
                            {
                                string text = File.ReadAllText(vdf);
                                MatchCollection matches = Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"");
                                foreach (Match m in matches)
                                {
                                    string lib = m.Groups[1].Value.Replace("\\\\", "\\");
                                    if (Directory.Exists(System.IO.Path.Combine(lib, @"steamapps\common\ARK")))
                                    {
                                        return lib;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (Directory.Exists(@"F:\Programs\Steam\steamapps\common\ARK"))
                return @"F:\Programs\Steam";

            return null;
        }

        private void CloseModal()
        {
            modalOverlay.Visibility = Visibility.Collapsed;
            modalOverlay.Children.Clear();
        }

        // =====================================================================
        // MAIN WINDOW CONTROLS
        // =====================================================================
        private Border CreateWindowControlButton(string symbol, bool isClose, Action onClick)
        {
            TextBlock tb = new TextBlock
            {
                Text = symbol,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Foreground = new SolidColorBrush(colMuted),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border b = new Border
            {
                Width = 36,
                Height = 28,
                Background = new SolidColorBrush(Color.FromArgb(140, 8, 28, 38)),
                BorderBrush = new SolidColorBrush(colBorderIdle),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = tb
            };

            b.MouseEnter += (s, e) =>
            {
                b.Background = new SolidColorBrush(isClose ? Color.FromRgb(140, 35, 35) : colTealFill);
                b.BorderBrush = new SolidColorBrush(isClose ? Color.FromRgb(200, 90, 90) : colBorderActive);
                tb.Foreground = new SolidColorBrush(colWhite);
            };
            b.MouseLeave += (s, e) =>
            {
                b.Background = new SolidColorBrush(Color.FromArgb(140, 8, 28, 38));
                b.BorderBrush = new SolidColorBrush(colBorderIdle);
                tb.Foreground = new SolidColorBrush(colMuted);
            };
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                onClick();
            };

            return b;
        }

        private Border CreateClusterButton(string title, string ip, out TextBlock labelOut, out Polygon arrowOut)
        {
            Grid g = new Grid
            {
                Margin = new Thickness(18, 0, 16, 0)
            };

            TextBlock tb = new TextBlock
            {
                Text = title,
                FontFamily = mainFont,
                FontSize = 23,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            g.Children.Add(tb);

            Polygon arrow = new Polygon
            {
                Points = new PointCollection(new Point[]
                {
                    new Point(0, 0),
                    new Point(12, 7),
                    new Point(0, 14)
                }),
                Fill = new SolidColorBrush(colWhite),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            g.Children.Add(arrow);

            Border b = new Border
            {
                Width = 194,
                Height = 46,
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                Child = g
            };

            b.MouseEnter += (s, e) =>
            {
                if (currentClusterIp != ip)
                {
                    b.Background = new SolidColorBrush(Color.FromArgb(180, 11, 76, 105));
                    b.BorderBrush = new SolidColorBrush(colBorderActive);
                }
            };
            b.MouseLeave += (s, e) =>
            {
                UpdateClusterVisuals();
            };
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                currentClusterIp = ip;
                UpdateClusterVisuals();
            };

            labelOut = tb;
            arrowOut = arrow;
            return b;
        }

        private void UpdateClusterVisuals()
        {
            bool is2Man = currentClusterIp == "46.4.188.156";

            activeIpLabel.Text = is2Man ? "2-Man   46.4.188.156" : "4-Man   5.9.117.144";

            btn2ManBorder.Background = new SolidColorBrush(is2Man ? colTealFill : colTealIdle);
            btn2ManBorder.BorderBrush = new SolidColorBrush(is2Man ? colBorderActive : colBorderIdle);
            btn2ManText.Foreground = new SolidColorBrush(is2Man ? colWhite : colMuted);
            btn2ManArrow.Visibility = is2Man ? Visibility.Visible : Visibility.Hidden;

            btn4ManBorder.Background = new SolidColorBrush(!is2Man ? colTealFill : colTealIdle);
            btn4ManBorder.BorderBrush = new SolidColorBrush(!is2Man ? colBorderActive : colBorderIdle);
            btn4ManText.Foreground = new SolidColorBrush(!is2Man ? colWhite : colMuted);
            btn4ManArrow.Visibility = !is2Man ? Visibility.Visible : Visibility.Hidden;
        }

        private Border CreateMapButton(MapEntry map)
        {
            Grid g = new Grid
            {
                Margin = new Thickness(12, 0, 14, 0)
            };

            StackPanel leftRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock numBlock = new TextBlock
            {
                Text = map.Number,
                FontFamily = mainFont,
                FontSize = 13,
                Foreground = new SolidColorBrush(colCyan),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 30
            };

            TextBlock nameBlock = new TextBlock
            {
                Text = map.Name,
                FontFamily = mainFont,
                FontSize = 19,
                Foreground = new SolidColorBrush(colWhite),
                VerticalAlignment = VerticalAlignment.Center
            };

            leftRow.Children.Add(numBlock);
            leftRow.Children.Add(nameBlock);
            g.Children.Add(leftRow);

            Polygon playArrow = new Polygon
            {
                Points = new PointCollection(new Point[]
                {
                    new Point(0, 0),
                    new Point(10, 6),
                    new Point(0, 12)
                }),
                Fill = new SolidColorBrush(colWhite),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Visibility = Visibility.Hidden
            };
            g.Children.Add(playArrow);

            Border b = new Border
            {
                Width = 265,
                Height = 36,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1.2),
                Cursor = Cursors.Hand,
                Child = g
            };

            b.MouseEnter += (s, e) =>
            {
                b.Background = new SolidColorBrush(colTealFill);
                b.BorderBrush = new SolidColorBrush(colBorderActive);
                playArrow.Visibility = Visibility.Visible;
            };

            b.MouseLeave += (s, e) =>
            {
                b.Background = Brushes.Transparent;
                b.BorderBrush = Brushes.Transparent;
                playArrow.Visibility = Visibility.Hidden;
            };

            int port = map.Port;
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                ConnectToServerIpPort(currentClusterIp, port);
            };

            return b;
        }

        private void ConnectToServerIpPort(string ip, int port)
        {
            string url = string.Format("steam://run/346110//+connect%20{0}:{1}", ip, port);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to launch Steam URI:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            app.Run(new LauncherWindow());
        }
    }
}
